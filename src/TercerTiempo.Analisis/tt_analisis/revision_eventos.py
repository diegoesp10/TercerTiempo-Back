"""Lo que de verdad pasa en el vídeo, anotado a mano en el visor, para medir lo que propone el Ojeador sin tocar su
propuesta: las jugadas (tiros, saques, paradas…) y las acciones de cada jugador que van al acta (pases, pérdidas…).

Un documento por vídeo, reconocido por su huella aunque el archivo cambie de carpeta:
- `hechos`: lo que pasó, con su tipo, su segundo y, en las acciones, quién la hizo (equipo y dorsal) si se sabe;
- `tramos`: los ratos revisados de principio a fin para un tipo: todo lo de ese tipo que pasó en ellos está en `hechos`;
- `decisiones`: por pasada (cada análisis distinto), el veredicto de cada propuesta: real, falsa (con su motivo) o
  dudosa («No se ve»).
Se guarda en un SQLite del equipo y, si se le da carpeta, también en una copia JSON por vídeo, que sirve para
recuperarlo si el SQLite se pierde."""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import sqlite3
from contextlib import closing
from pathlib import Path
from uuid import uuid4

# Las jugadas salen de `final["eventos"]`. Se anotan en el segundo que usa el Ojeador (`PISTAS`): en un córner o un
# saque, el de la salida del balón, no el del saque. Fuera quedan las que no dicen qué pasó (balón fuera por el fondo sin
# saber quién lo tocó) y las de segundo dudoso (faltas, fueras de juego, saques de centro).
JUGADAS = {"gol": "Gol", "tiro": "Tiro", "parada": "Parada", "saque_puerta": "Saque de puerta", "corner": "Córner", "saque_banda": "Saque de banda"}
# Las acciones de cada jugador que van al acta, de las listas de segundos de `final["acciones"]`. Las paradas ya están en
# las jugadas.
LISTAS_DE_ACCIONES = {"pases": "pase", "pases_fallados": "pase_fallado", "perdidas": "perdida", "recuperaciones": "recuperacion", "saques": "saque_portero"}
ACCIONES = {"pase": "Pase", "pase_fallado": "Pase fallado", "perdida": "Pérdida", "recuperacion": "Recuperación", "saque_portero": "Saque del portero"}
TIPOS = {**JUGADAS, **ACCIONES}
PISTAS = {
    "gol": "El segundo en que el balón cruza la línea de gol.",
    "tiro": "El segundo del disparo, cuando el balón sale hacia la portería.",
    "parada": "El segundo en que el portero para el balón del rival o se queda con él.",
    "saque_puerta": "El segundo en que el balón sale por la línea de fondo tocado por un atacante, no el del saque.",
    "corner": "El segundo en que el balón sale por la línea de fondo tocado por un defensor, no el del saque.",
    "saque_banda": "El segundo en que el balón sale por la banda, no el del saque.",
    "pase": "El segundo en que sale el pase, de un jugador a otro de su equipo.",
    "pase_fallado": "El segundo en que sale el pase que no llega a nadie de su equipo.",
    "perdida": "El segundo en que el rival le quita el balón o lo pierde.",
    "recuperacion": "El segundo en que le roba el balón al rival y se queda con él.",
    "saque_portero": "El segundo en que el portero saca, con el pie o con la mano.",
}
# Lo más que puede separarse una propuesta de lo que pasó para darla por buena. Los pases van muy seguidos: con 2 s, uno
# se emparejaría con el siguiente.
MARGEN_S = 2.0
MARGENES = {**{t: MARGEN_S for t in JUGADAS}, **{t: 1.0 for t in ACCIONES}}
MOTIVOS = {
    "otra_accion": "Era otra cosa (un centro, un despeje…)",
    "repetida": "Repetida",
    "balon_falso": "Balón falso",
    "balon_no_visto": "No veía el balón",
    "salto_seguidor": "Salto del seguidor",
    "otro": "Otro",
}
ESTADOS = ("real", "falsa", "dudosa", "pendiente")
# Quién hizo una acción real: el que dice el Ojeador, otro jugador o no se ve.
QUIEN = ("bien", "otro", "no_se_sabe")
FORMATO = 1
HUELLA = re.compile(r"[0-9a-f]{64}")


class ConflictoRevision(ValueError):
    pass


def huella_video(ruta: Path) -> str:
    """Identifica el archivo por su tamaño y muestras de bytes, aunque se mueva de carpeta."""
    with ruta.open("rb") as archivo:
        tamano = archivo.seek(0, 2)
        huella = hashlib.sha256(str(tamano).encode("ascii"))
        for offset in (0, max(0, tamano // 2 - 32768), max(0, tamano - 65536)):
            archivo.seek(offset)
            huella.update(archivo.read(65536))
    return huella.hexdigest()


def catalogo() -> dict:
    """Lo que la página necesita para enseñar los tipos, los motivos y el segundo de cada tipo."""
    return {"tipos": TIPOS, "jugadas": list(JUGADAS), "acciones": list(ACCIONES), "pistas": PISTAS, "margenes": MARGENES, "motivos": MOTIVOS}


def _json(valor) -> str:
    return json.dumps(valor, ensure_ascii=False, allow_nan=False, sort_keys=True, separators=(",", ":"))


def _segundo(valor) -> float:
    if type(valor) not in (int, float) or not 0 <= valor <= 86400 or not math.isfinite(valor):
        raise ValueError("Segundo del vídeo no válido.")
    return round(valor, 3)


def _entero(texto) -> int | None:
    try:
        return int(texto)
    except (TypeError, ValueError):
        return None


def _nombres_de_equipo(final: dict) -> dict[int, str]:
    nombres = {0: "Equipo A", 1: "Equipo B"}
    for k, equipo in enumerate(final.get("equipos") or []):
        if isinstance(equipo, dict) and equipo.get("nombre"):
            nombres[equipo.get("id", k)] = str(equipo["nombre"])
    return nombres


def _quien_es(jugador: dict | None, nombres: dict[int, str]) -> dict | None:
    """Un jugador identificado como se compara entre pasadas: por equipo y dorsal (los números internos cambian)."""
    if not jugador or type(jugador.get("dorsal")) is not int or jugador.get("equipo") not in nombres:
        return None
    return {"equipo": nombres[jugador["equipo"]], "dorsal": jugador["dorsal"], "nombre": jugador.get("nombre") or ""}


def clave_jugador(jugador: dict | None) -> dict | None:
    return None if not jugador else {"equipo": jugador["equipo"], "dorsal": jugador["dorsal"]}


def fuente_revision(final: dict) -> dict:
    """Las propuestas de una pasada que se pueden revisar. `pasada` las identifica: dos análisis que proponen lo mismo
    en el mismo tramo comparten los veredictos, aunque sean de versiones distintas (la 2.4.0 propone lo mismo que la
    2.3.2, solo más deprisa)."""
    video = final["video"]
    desde, hasta = _segundo(video["tramo_inicio_s"]), _segundo(video["tramo_fin_s"])
    nombres = _nombres_de_equipo(final)
    candidatos = [
        {"id": i, "tipo": e["tipo"], "t": _segundo(e["t"]), "equipo": e.get("equipo"),
         "detalle": e.get("detalle") or "", "confianza": e.get("confianza"), "tracklet": e.get("tracklet")}
        for i, e in enumerate(final.get("eventos") or [])
        if e.get("tipo") in JUGADAS and desde <= e["t"] <= hasta
    ]
    jugadores = {j["id"]: j for j in final.get("jugadores") or [] if isinstance(j, dict) and "id" in j}
    for clave, hizo in (final.get("acciones") or {}).items():
        jugador = jugadores.get(_entero(clave))
        equipo = hizo.get("equipo") if jugador is None else jugador.get("equipo")
        quien = _quien_es(jugador, nombres)
        for lista, tipo in LISTAS_DE_ACCIONES.items():
            for k, t in enumerate(hizo.get(lista) or []):
                if desde <= t <= hasta:
                    candidatos.append({"id": f"{tipo}:{clave}:{k}", "tipo": tipo, "t": _segundo(t), "equipo": equipo, "jugador": quien})
    candidatos.sort(key=lambda e: e["t"])
    analizador = (final.get("analizador") or {}).get("version")
    clave = hashlib.sha256(_json([desde, hasta, candidatos]).encode("utf-8")).hexdigest()
    plantilla = sorted(
        ({**quien, "equipo_id": j.get("equipo")} for j in jugadores.values() if (quien := _quien_es(j, nombres))),
        key=lambda q: (q["equipo_id"], q["dorsal"]),
    )
    return {
        "pasada": clave, "version": analizador, "desde": desde, "hasta": hasta,
        "equipos": nombres, "jugadores": plantilla, "candidatos": candidatos,
    }


class Revisiones:
    """Un documento por vídeo. La revisión de edición impide pisar cambios de otra ventana."""

    def __init__(self, ruta: Path, copias: Path | None = None) -> None:
        self.ruta = ruta
        self.copias = copias

    def _conectar(self):
        self.ruta.parent.mkdir(parents=True, exist_ok=True)
        conexion = sqlite3.connect(self.ruta)
        try:
            conexion.execute("CREATE TABLE IF NOT EXISTS revisiones (video TEXT PRIMARY KEY, datos TEXT NOT NULL)")
        except BaseException:
            conexion.close()
            raise
        return conexion

    def _copia(self, video: str) -> Path | None:
        if self.copias is None or not HUELLA.fullmatch(video):
            return None
        return self.copias / f"{video}.json"

    def _recuperar(self, video: str) -> dict | None:
        ruta = self._copia(video)
        if ruta is None:
            return None
        try:
            datos = json.loads(ruta.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None
        validos = isinstance(datos, dict) and datos.get("formato") == FORMATO and datos.get("video") == video
        return datos if validos and all(k in datos for k in ("rev", "hechos", "tramos", "decisiones")) else None

    def _leer(self, conexion, video: str) -> dict:
        fila = conexion.execute("SELECT datos FROM revisiones WHERE video = ?", (video,)).fetchone()
        if fila:
            return json.loads(fila[0])
        return self._recuperar(video) or {"formato": FORMATO, "video": video, "rev": 0, "hechos": [], "tramos": [], "decisiones": {}}

    def leer(self, video: str) -> dict:
        with closing(self._conectar()) as conexion:
            return self._leer(conexion, video)

    def editar(self, video: str, fuente: dict, orden: dict) -> dict:
        if not isinstance(orden, dict):
            raise ValueError("La revisión no es válida.")
        if orden.get("pasada") != fuente["pasada"] or orden.get("video") != video:
            raise ConflictoRevision("Cambió el vídeo o el análisis. Vuelve a abrirlo.")
        with closing(self._conectar()) as conexion, conexion:
            conexion.execute("BEGIN IMMEDIATE")
            datos = self._leer(conexion, video)
            if type(orden.get("rev")) is not int or orden["rev"] != datos["rev"]:
                raise ConflictoRevision("Hay cambios en otra ventana. Actualiza la revisión.")
            _editar(datos, fuente, orden)
            datos["rev"] += 1
            conexion.execute("INSERT INTO revisiones VALUES (?, ?) ON CONFLICT(video) DO UPDATE SET datos=excluded.datos", (video, _json(datos)))
            return datos

    def guardar_copia(self, datos: dict) -> str | None:
        """Copia el documento en JSON junto a los demás datos. Devuelve por qué no se ha podido: sin copia, lo guardado
        en el SQLite sigue ahí."""
        ruta = self._copia(datos["video"])
        if ruta is None:
            return None
        try:
            ruta.parent.mkdir(parents=True, exist_ok=True)
            temporal = ruta.with_name(ruta.name + ".tmp")
            temporal.write_text(json.dumps(datos, ensure_ascii=False, indent=1), encoding="utf-8")
            os.replace(temporal, ruta)
        except OSError as error:
            return str(error)
        return None


def _tipo(orden):
    tipo = orden.get("tipo")
    if not isinstance(tipo, str) or tipo not in TIPOS:
        raise ValueError("Tipo de acción no válido.")
    return tipo


def _en_tramo(fuente, valor):
    segundo = _segundo(valor)
    if not fuente["desde"] <= segundo <= fuente["hasta"]:
        raise ValueError("Ese segundo queda fuera del tramo analizado.")
    return segundo


def _jugador(valor, tipo):
    """El jugador que dice la página (equipo y dorsal), solo en las acciones."""
    if valor is None:
        return None
    if tipo not in ACCIONES:
        raise ValueError("Solo las acciones de un jugador llevan quién la hizo.")
    equipo, dorsal = (valor.get("equipo"), valor.get("dorsal")) if isinstance(valor, dict) else (None, None)
    if not isinstance(equipo, str) or not 0 < len(equipo) <= 100 or type(dorsal) is not int or not 0 <= dorsal <= 99:
        raise ValueError("Jugador no válido.")
    return {"equipo": equipo, "dorsal": dorsal}


def _hecho(datos, tipo, t, nota, origen, jugador=None):
    hecho = {"id": uuid4().hex, "tipo": tipo, "t": t, "nota": nota, "origen": origen}
    if tipo in ACCIONES:
        hecho["jugador"] = jugador
    datos["hechos"].append(hecho)
    return hecho["id"]


def _decisiones(datos):
    return (d for pasada in datos["decisiones"].values() for d in pasada["marcas"].values())


def _enlazar(datos, marcas, evento, punto, exacto, nota):
    """El hecho de una propuesta real: uno ya anotado cerca que no sea de otra propuesta de esta misma pasada (dos tiros
    seguidos son dos hechos) o uno nuevo. Con el segundo exacto, el hecho que salió del segundo de otra propuesta pasa a
    ese segundo; lo anotado a mano no se mueve."""
    ocupados = {d["hecho"] for d in marcas.values() if d.get("hecho")}
    libres = [
        h for h in datos["hechos"]
        if h["tipo"] == evento["tipo"] and h["id"] not in ocupados and abs(h["t"] - punto) <= MARGENES[evento["tipo"]]
    ]
    if not libres:
        return _hecho(datos, evento["tipo"], punto, nota, "exacta" if exacto else "confirmada")
    hecho = min(libres, key=lambda h: abs(h["t"] - punto))
    if exacto and hecho["origen"] == "confirmada":
        hecho["t"], hecho["origen"] = punto, "exacta"
    return hecho["id"]


def _apuntar_quien(datos, ident, evento, quien, jugador):
    """Lo que se sabe de quién hizo la acción: el jugador, si se sabe, y los que se sabe que no fueron. Manda lo último
    que se dice; «No se ve quién» deja de dar al jugador por sabido, pero no olvida los que no fueron."""
    hecho = next(h for h in datos["hechos"] if h["id"] == ident)
    suyo = clave_jugador(evento.get("jugador"))
    no_es = hecho.setdefault("no_es", [])
    if quien == "bien":
        hecho["jugador"] = suyo
        hecho["no_es"] = [j for j in no_es if j != suyo]
    elif quien == "otro":
        if jugador is not None:
            hecho["jugador"] = jugador
        elif hecho.get("jugador") == suyo:
            hecho["jugador"] = None
        if suyo is not None and suyo not in no_es:
            no_es.append(suyo)
    else:
        hecho["jugador"] = None
    if not hecho["no_es"]:
        del hecho["no_es"]


def _decidir(datos, fuente, orden, nota):
    ident, estado = orden.get("id"), orden.get("estado")
    evento = next((e for e in fuente["candidatos"] if type(e["id"]) is type(ident) and e["id"] == ident), None)
    if evento is None or estado not in ESTADOS:
        raise ValueError("Candidato o veredicto no válido.")
    tipo = evento["tipo"]
    motivo = orden.get("motivo")
    if motivo is not None and (estado != "falsa" or motivo not in MOTIVOS):
        raise ValueError("El motivo solo se da al marcarla como falsa y tiene que ser uno de la lista.")
    quien = orden.get("quien")
    if quien is not None and (estado != "real" or tipo not in ACCIONES or quien not in QUIEN or (quien == "bien" and not evento.get("jugador"))):
        raise ValueError("Quién la hizo solo se dice de una acción real de un jugador.")
    if orden.get("jugador") is not None and quien != "otro":
        raise ValueError("El jugador solo se da cuando fue otro.")
    jugador = _jugador(orden.get("jugador"), tipo)
    if jugador is not None and jugador == clave_jugador(evento.get("jugador")):
        raise ValueError("Si la hizo el jugador que dice la propuesta, márcalo como que fue él.")
    exacto = orden.get("t") is not None
    punto = evento["t"]
    if exacto:
        if estado != "real":
            raise ValueError("El segundo exacto solo se da al marcarla como real.")
        punto = _en_tramo(fuente, orden.get("t"))
        if abs(punto - evento["t"]) > MARGENES[tipo]:
            raise ValueError(f"Lo que pasó queda a más de {MARGENES[tipo]:g} s de la propuesta: márcala como falsa y anótalo aparte.")
    marcas = datos["decisiones"].setdefault(fuente["pasada"], {"version": fuente["version"], "marcas": {}})["marcas"]
    anterior = marcas.pop(str(ident), {}).get("hecho")
    if estado != "pendiente":
        marca = {"estado": estado, "evento": evento, "nota": nota, "hecho": None}
        if estado == "real":
            marca["hecho"] = _enlazar(datos, marcas, evento, punto, exacto, nota)
            if quien is not None:
                marca["quien"] = quien
                _apuntar_quien(datos, marca["hecho"], evento, quien, jugador)
        if motivo is not None:
            marca["motivo"] = motivo
        marcas[str(ident)] = marca
    if anterior and not any(d["hecho"] == anterior for d in _decisiones(datos)):
        datos["hechos"] = [h for h in datos["hechos"] if h["id"] != anterior or h["origen"] == "manual"]


def _editar(datos, fuente, orden):
    accion = orden.get("accion")
    nota = orden.get("nota", "")
    if not isinstance(nota, str) or len(nota) > 500:
        raise ValueError("La nota puede tener hasta 500 caracteres.")
    nota = nota.strip()
    if accion == "decidir":
        _decidir(datos, fuente, orden, nota)
    elif accion == "anotar":
        tipo, t = _tipo(orden), _en_tramo(fuente, orden.get("t"))
        if any(h["tipo"] == tipo and h["t"] == t for h in datos["hechos"]):
            raise ValueError("Esa acción ya está anotada en ese segundo.")
        _hecho(datos, tipo, t, nota, "manual", _jugador(orden.get("jugador"), tipo))
    elif accion == "cubrir":
        tipo = _tipo(orden)
        desde, hasta = (_en_tramo(fuente, orden.get(k)) for k in ("desde", "hasta"))
        if desde >= hasta:
            raise ValueError("El final del tramo debe ir después del inicio.")
        datos["tramos"].append({"id": uuid4().hex, "tipo": tipo, "desde": desde, "hasta": hasta})
    elif accion in ("quitar_hecho", "quitar_tramo"):
        clave = "hechos" if accion == "quitar_hecho" else "tramos"
        ident = orden.get("id")
        if not any(fila["id"] == ident for fila in datos[clave]):
            raise ValueError("Esa anotación ya no existe.")
        datos[clave] = [fila for fila in datos[clave] if fila["id"] != ident]
        if clave == "hechos":
            for pasada in datos["decisiones"].values():
                pasada["marcas"] = {i: d for i, d in pasada["marcas"].items() if d["hecho"] != ident}
    else:
        raise ValueError("Operación de revisión no válida.")
