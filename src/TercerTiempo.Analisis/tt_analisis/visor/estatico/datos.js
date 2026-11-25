// Los datos de un análisis en el visor: lo de todo el tramo (final.json y analisis.json) y los fotogramas, que se piden
// por tramos de VENTANA_S segundos alrededor de donde está el vídeo.

import { TIPOS_ACCION, buscarIndice } from './util.js';

const VENTANA_S = 20;
// Entre dos fotogramas analizados (0,2 s a 5 por segundo) se interpola; con un hueco mayor, solo cerca de uno de ellos.
const HUECO_MAX_S = 0.6;
const CERCA_S = 0.15;
// El balón se interpola entre dos avistamientos si están a menos de esto; si no, solo se enseña cerca de uno.
const BALON_HUECO_S = 0.45;
const BALON_CERCA_S = 0.12;

export async function pedirJSON(url, opciones) {
  const r = await fetch(url, opciones);
  const datos = await r.json().catch(() => null);
  if (!r.ok) throw new Error((datos && datos.error) || `Error ${r.status}`);
  return datos;
}

export class Repeticion {
  constructor(id, ficha, final, analisis, acta) {
    this.id = id;
    this.ficha = ficha;
    this.final = final;
    this.analisis = analisis || {};
    this.acta = acta;
    const ins = final.instantes;
    this.t = Float64Array.from(ins.t);
    this.balon = ins.balon;
    this.enJuego = ins.en_juego;
    this.posesion = ins.posesion;
    this.calibrado = ins.calibrado;
    this.mitad = ins.mitad;
    this.dt = final.dt;
    this.video = final.video;
    this.inicio = final.video.tramo_inicio_s;
    this.fin = final.video.tramo_fin_s;
    this.equipos = final.equipos;
    this.campo = final.campo;
    this.jugadores = new Map(final.jugadores.map((j) => [j.id, j]));
    this.tracklets = new Map(Object.entries(final.tracklets || {}).map(([k, v]) => [Number(k), v]));
    this.resumenes = new Map((this.analisis.tracklets || []).map((r) => [r.tracklet, r]));
    this.toques = final.toques.map(([t0, t1, id, equipo, parado]) => ({ t0, t1, id, equipo, parado: !!parado })).sort((a, b) => a.t0 - b.t0);
    this.toquesT0 = this.toques.map((x) => x.t0);
    this.eventos = final.eventos.slice().sort((a, b) => a.t - b.t);
    this.momentos = [];
    for (const [id, a] of Object.entries(final.acciones || {})) {
      for (const tipo of Object.keys(TIPOS_ACCION)) {
        for (const t of a[tipo] || []) this.momentos.push({ t, tipo, id: Number(id), equipo: a.equipo });
      }
    }
    this.momentos.sort((a, b) => a.t - b.t);
    this.momentosT = this.momentos.map((m) => m.t);
    this.eventosT = this.eventos.map((e) => e.t);
    this._prefijos();
    this.fotogramas = [];
    this.fotogramasT = [];
    this.balones = [];
    this.balonesT = [];
    this.descartados = [];
    this.ventanas = new Map();
    this.alCargar = null;
  }

  _prefijos() {
    const n = this.t.length;
    const nuevo = () => new Int32Array(n + 1);
    this.p = { juego: nuevo(), vistoJuego: nuevo(), trayJuego: nuevo(), pos0: nuevo(), pos1: nuevo(), calib: nuevo(), visto: nuevo(), tray: nuevo() };
    const p = this.p;
    for (let k = 0; k < n; k++) {
      const juego = this.enJuego[k] === '1';
      const b = this.balon[k];
      const visto = b === 'v' || b === 'n';
      const tray = visto || b === 'i';
      const q = this.posesion[k];
      p.juego[k + 1] = p.juego[k] + (juego ? 1 : 0);
      p.vistoJuego[k + 1] = p.vistoJuego[k] + (juego && visto ? 1 : 0);
      p.trayJuego[k + 1] = p.trayJuego[k] + (juego && tray ? 1 : 0);
      p.pos0[k + 1] = p.pos0[k] + (q === '0' ? 1 : 0);
      p.pos1[k + 1] = p.pos1[k] + (q === '1' ? 1 : 0);
      p.calib[k + 1] = p.calib[k] + (this.calibrado[k] === '1' ? 1 : 0);
      p.visto[k + 1] = p.visto[k] + (visto ? 1 : 0);
      p.tray[k + 1] = p.tray[k] + (tray ? 1 : 0);
    }
  }

  // Índice del último instante analizado en `t` o antes (-1 antes del primero).
  instante(t) {
    return buscarIndice(this.t, t);
  }

  // Lo acumulado desde el principio del tramo hasta `t`, como lo cuenta el analizador.
  hasta(t) {
    const k = this.instante(t) + 1;
    const p = this.p;
    return {
      n: k,
      juego: p.juego[k],
      vistoJuego: p.vistoJuego[k],
      trayJuego: p.trayJuego[k],
      pos: [p.pos0[k], p.pos1[k]],
      calib: p.calib[k],
      visto: p.visto[k],
      tray: p.tray[k],
    };
  }

  momentosHasta(t) {
    return this.momentos.slice(0, buscarIndice(this.momentosT, t) + 1);
  }

  eventosHasta(t) {
    return this.eventos.slice(0, buscarIndice(this.eventosT, t) + 1);
  }

  toqueEn(t) {
    const i = buscarIndice(this.toquesT0, t);
    for (let k = i; k >= 0 && k > i - 3; k--) {
      const x = this.toques[k];
      if (x.t0 <= t && t <= x.t1 + 0.1) return x;
    }
    return null;
  }

  // Pide los fotogramas de alrededor de `t` (los que falten). Devuelve una promesa que se cumple cuando están.
  asegurar(t) {
    const relativo = t - this.inicio;
    const i = Math.floor(relativo / VENTANA_S);
    const pedir = [i, i + 1];
    if (relativo - i * VENTANA_S < 4) pedir.push(i - 1);
    const validas = pedir.filter((k) => k >= 0 && this.inicio + k * VENTANA_S <= this.fin + 0.01);
    return Promise.all(validas.map((k) => this._ventana(k)));
  }

  cargado(t) {
    const i = Math.floor((t - this.inicio) / VENTANA_S);
    return this.ventanas.get(i)?.listo === true;
  }

  _ventana(k) {
    let v = this.ventanas.get(k);
    if (!v) {
      const desde = this.inicio + k * VENTANA_S;
      v = { listo: false };
      v.promesa = pedirJSON(`/api/a/${this.id}/fotogramas?desde=${desde.toFixed(3)}&hasta=${(desde + VENTANA_S).toFixed(3)}`)
        .then((lista) => {
          this._anadir(lista);
          v.listo = true;
          if (this.alCargar) this.alCargar();
        })
        .catch((e) => {
          this.ventanas.delete(k);
          throw e;
        });
      this.ventanas.set(k, v);
    }
    return v.promesa;
  }

  _anadir(lista) {
    const conocidos = new Set(this.fotogramas.map((f) => f.i));
    const nuevos = lista.filter((f) => !conocidos.has(f.i));
    if (!nuevos.length) return;
    this.fotogramas = this.fotogramas.concat(nuevos).sort((a, b) => a.t - b.t);
    this.fotogramasT = this.fotogramas.map((f) => f.t);
    for (const f of nuevos) {
      if (f.b) {
        this.balones.push({ t: f.t, x: f.b[0], y: f.b[1], o: f.b[2], c: f.b[3], bc: f.bc || null });
        if (f.b[2] === 'n' && f.v) {
          for (const v of f.v) this.balones.push({ t: v[0], x: v[1], y: v[2], o: 'n', c: v[3], bc: null, vecino: true });
        }
      }
      if (f.bd) this.descartados.push({ t: f.t, x: f.bd[0], y: f.bd[1], c: f.bd[2] });
    }
    this.balones.sort((a, b) => a.t - b.t);
    this.balonesT = this.balones.map((b) => b.t);
    this.descartados.sort((a, b) => a.t - b.t);
  }

  // Los dos fotogramas analizados entre los que cae `t` y cuánto se ha avanzado del uno al otro.
  par(t) {
    const k = buscarIndice(this.fotogramasT, t);
    const a = k >= 0 ? this.fotogramas[k] : null;
    const b = this.fotogramas[k + 1] || null;
    if (!a) return { a: null, b: null, alfa: 0, cerca: b && b.t - t <= CERCA_S ? b : null };
    if (!b || b.t - a.t > HUECO_MAX_S) return { a, b: null, alfa: 0, cerca: t - a.t <= CERCA_S ? a : (b && b.t - t <= CERCA_S ? b : null) };
    const alfa = (t - a.t) / (b.t - a.t);
    return { a, b, alfa, cerca: alfa < 0.5 ? a : b };
  }

  // Las personas en `t`, con la caja interpolada entre los dos fotogramas analizados de alrededor.
  personasEn(t) {
    const { a, b, alfa, cerca } = this.par(t);
    if (!a || !b) return cerca ? cerca.p.map((p) => persona(p, cerca)) : [];
    const deB = new Map(b.p.map((p) => [p[1], p]));
    const salida = [];
    for (const pa of a.p) {
      const pb = deB.get(pa[1]);
      if (pb) {
        deB.delete(pa[1]);
        const x = persona(alfa < 0.5 ? pa : pb, alfa < 0.5 ? a : b);
        x.caja = pa.slice(4, 8).map((v, i) => v + (pb[4 + i] - v) * alfa);
        salida.push(x);
      } else if (alfa < 0.5) {
        salida.push(persona(pa, a));
      }
    }
    if (alfa >= 0.5) for (const pb of deB.values()) salida.push(persona(pb, b));
    return salida;
  }

  // El balón en `t`: entre dos avistamientos cercanos, interpolado; si no, el más cercano si está muy cerca.
  balonEn(t) {
    const k = buscarIndice(this.balonesT, t);
    const a = k >= 0 ? this.balones[k] : null;
    const b = this.balones[k + 1] || null;
    if (a && b && b.t - a.t <= BALON_HUECO_S) {
      const f = b.t > a.t ? (t - a.t) / (b.t - a.t) : 0;
      const o = a.o === 'i' && b.o === 'i' ? 'i' : (f < 0.5 ? a.o : b.o);
      return { x: a.x + (b.x - a.x) * f, y: a.y + (b.y - a.y) * f, o };
    }
    if (a && t - a.t <= BALON_CERCA_S) return { x: a.x, y: a.y, o: a.o };
    if (b && b.t - t <= BALON_CERCA_S) return { x: b.x, y: b.y, o: b.o };
    return null;
  }

  estela(t, segundos) {
    const k = buscarIndice(this.balonesT, t);
    const salida = [];
    for (let i = k; i >= 0 && this.balones[i].t >= t - segundos; i--) salida.push(this.balones[i]);
    return salida.reverse();
  }

  balonCampoEn(t) {
    const { a, b, alfa, cerca } = this.par(t);
    if (a && b && a.bc && b.bc) return [a.bc[0] + (b.bc[0] - a.bc[0]) * alfa, a.bc[1] + (b.bc[1] - a.bc[1]) * alfa];
    return cerca && cerca.bc ? cerca.bc : null;
  }

  descartadosCerca(t, margen = 0.12) {
    return this.descartados.filter((d) => Math.abs(d.t - t) <= margen);
  }

  // El primer momento desde `t` en que sale este id (en las cajas cargadas), para ir a él.
  siguienteAparicion(id, t) {
    const k = Math.max(0, buscarIndice(this.fotogramasT, t));
    for (let i = k; i < this.fotogramas.length; i++) {
      if (this.fotogramas[i].p.some((p) => p[0] === id)) return this.fotogramas[i].t;
    }
    return null;
  }

  nombreDe(id) {
    const j = this.jugadores.get(id);
    if (j) return j.nombre ? `${j.dorsal} · ${nombreCorto(j.nombre)}` : `${j.dorsal}`;
    return id == null ? '' : `#${id}`;
  }
}

function persona(p, fotograma) {
  return { id: p[0], tracklet: p[1], clase: p[2], equipo: p[3], caja: p.slice(4, 8), seguidor: p.length > 8 ? p[8] : p[1], h: fotograma.h };
}

// «GARCIA ALONSO, ALEJANDRO» → «Alejandro García»: el nombre y el primer apellido, para que quepa.
export function nombreCorto(nombre) {
  if (!nombre) return '';
  const [apellidos, propio] = nombre.split(',').map((s) => s.trim());
  const capital = (s) => s.toLowerCase().replace(/(^|[\s-])(\p{L})/gu, (m, a, b) => a + b.toUpperCase());
  if (!propio) return capital(apellidos);
  return `${capital(propio.split(/\s+/)[0])} ${capital(apellidos.split(/\s+/)[0])}`;
}
