// El panel de la derecha: las estadísticas tal como las lleva el analizador hasta donde está el vídeo, los jugadores,
// las jugadas y todo lo que dejó el análisis.

import { nombreCorto } from './datos.js';
import { ACCIONES_PLURAL, COLORES, METODO, ORIGEN_BALON, TIPOS_ACCION, TIPOS_EVENTO, duracion, el, minuto, numero, pct } from './util.js';

const EVENTOS_CONTADOS = ['gol', 'tiro', 'corner', 'falta', 'fuera_de_juego', 'saque_banda', 'saque_puerta', 'fuera_fondo'];

export class Panel {
  constructor(cuerpo, rep, acciones) {
    this.cuerpo = cuerpo;
    this.rep = rep;
    this.ir = acciones.ir;
    this.elegir = acciones.elegir;
    this.pestana = 'estadisticas';
    this.filtro = { jugadas: true, acciones: true };
    this.ultimaClave = null;
  }

  mostrar(pestana) {
    this.pestana = pestana;
    this.ultimaClave = null;
  }

  // Se llama a menudo: solo se vuelve a pintar si ha cambiado algo de lo que se enseña.
  actualizar(t, seleccion) {
    const rep = this.rep;
    const k = rep.instante(t);
    const clave = `${this.pestana}|${this.pestana === 'analisis' ? '' : k}|${seleccion}|${this.filtro.jugadas}${this.filtro.acciones}`;
    if (clave === this.ultimaClave) return;
    this.ultimaClave = clave;
    const scroll = this.cuerpo.scrollTop;
    this.cuerpo.replaceChildren(...this[`_${this.pestana}`](t, seleccion));
    this.cuerpo.scrollTop = scroll;
  }

  _nombreEquipo(e) {
    return this.rep.equipos[e]?.nombre || `Equipo ${e === 0 ? 'A' : 'B'}`;
  }

  _estadisticas(t) {
    const rep = this.rep;
    const tt = Math.min(Math.max(t, rep.inicio), rep.fin);
    const h = rep.hasta(tt);
    const dt = rep.dt;
    const nodos = [];
    nodos.push(el('div', { clase: 'hasta num', texto: `Hasta el ${minuto(tt)}` }));
    nodos.push(el('div', { clase: 'sub', texto: `${duracion(tt - rep.inicio)} de los ${duracion(rep.fin - rep.inicio)} analizados · se cuenta como lo cuenta el analizador` }));

    const conDueno = h.pos[0] + h.pos[1];
    const p0 = conDueno ? (100 * h.pos[0]) / conDueno : 50;
    nodos.push(el('h2', { texto: 'Posesión' }));
    nodos.push(el('div', { clase: 'posesion' },
      el('div', { clase: 'cifras num' }, el('span', { texto: conDueno ? `${Math.round(p0)} %` : '–' }), el('span', { texto: conDueno ? `${100 - Math.round(p0)} %` : '–' })),
      el('div', { clase: 'barra-pos' }, el('span', { estilo: { width: `${p0}%`, background: COLORES.equipo[0] } }), el('span', { estilo: { width: `${100 - p0}%`, background: COLORES.equipo[1] } })),
      el('div', { clase: 'nombres' }, el('span', { texto: this._nombreEquipo(0) }), el('span', { texto: this._nombreEquipo(1) })),
      el('div', { clase: 'sub', texto: `Sobre ${duracion(conDueno * dt)} con el balón en juego, a la vista y con dueño: cada instante es del último equipo que lo tocó.` }),
    ));

    nodos.push(el('h2', { texto: 'El balón' }));
    nodos.push(medida('En juego', pct(h.juego, h.n), h.juego / Math.max(1, h.n), '#94a3b8', `${duracion(h.juego * dt)} de ${duracion(h.n * dt)}`));
    nodos.push(medida('Visto con el balón en juego', pct(h.vistoJuego, h.juego), h.vistoJuego / Math.max(1, h.juego), COLORES.balon.v, 'En su fotograma o en uno vecino (±0,04 s).'));
    nodos.push(medida('Con trayectoria en juego', pct(h.trayJuego, h.juego), h.trayJuego / Math.max(1, h.juego), COLORES.balon.i, 'Visto o interpolado entre dos avistamientos cercanos.'));
    nodos.push(medida('Campo calibrado', pct(h.calib, h.n), h.calib / Math.max(1, h.n), COLORES.calibrado, 'Sin calibrar no se sabe dónde está cada uno en el campo.'));

    const momentos = rep.momentosHasta(tt);
    const cuenta = { 0: {}, 1: {} };
    for (const m of momentos) if (m.equipo === 0 || m.equipo === 1) cuenta[m.equipo][m.tipo] = (cuenta[m.equipo][m.tipo] || 0) + 1;
    nodos.push(el('h2', { texto: 'Lo que cuenta para el acta' }));
    nodos.push(tablaEquipos(this, Object.keys(ACCIONES_PLURAL).map((tipo) => [ACCIONES_PLURAL[tipo], cuenta[0][tipo] || 0, cuenta[1][tipo] || 0])));

    const eventos = rep.eventosHasta(tt);
    const porTipo = { 0: {}, 1: {} };
    for (const e of eventos) if (e.equipo === 0 || e.equipo === 1) porTipo[e.equipo][e.tipo] = (porTipo[e.equipo][e.tipo] || 0) + 1;
    const filas = EVENTOS_CONTADOS.filter((tipo) => eventos.some((e) => e.tipo === tipo)).map((tipo) => [TIPOS_EVENTO[tipo], porTipo[0][tipo] || 0, porTipo[1][tipo] || 0]);
    nodos.push(el('h2', { texto: 'Jugadas' }));
    nodos.push(filas.length ? tablaEquipos(this, filas) : el('div', { clase: 'sub', texto: 'Ninguna todavía.' }));

    nodos.push(el('h2', { texto: 'Lo último' }));
    const ultimos = [...momentos.slice(-8).map((m) => ({ t: m.t, texto: `${TIPOS_ACCION[m.tipo]} · ${this._quien(m.id, m.equipo)}`, id: m.id })),
      ...eventos.filter((e) => e.tipo !== 'paron').slice(-5).map((e) => ({ t: e.t, texto: `${TIPOS_EVENTO[e.tipo] || e.tipo}${e.equipo != null ? ' · ' + this._nombreEquipo(e.equipo) : ''}`, id: e.tracklet }))]
      .sort((a, b) => b.t - a.t).slice(0, 8);
    nodos.push(ultimos.length ? el('div', { clase: 'filas' }, ultimos.map((u) => fila(u, false, false, () => this.ir(u.t - 2, u.id)))) : el('div', { clase: 'sub', texto: 'Nada todavía.' }));

    nodos.push(el('h2', { texto: 'Cómo leer el vídeo' }));
    nodos.push(leyenda());
    return nodos;
  }

  _quien(id, equipo) {
    const j = this.rep.jugadores.get(id);
    if (j) return `${j.dorsal}${j.nombre ? ' ' + nombreCorto(j.nombre) : ''}`;
    return `sin identificar (${this._nombreEquipo(equipo)})`;
  }

  _jugadores(t, seleccion) {
    const rep = this.rep;
    const nodos = [];
    if (seleccion != null) nodos.push(this._ficha(seleccion, t));
    else nodos.push(el('div', { clase: 'sub', texto: 'Pulsa un jugador (aquí o en el vídeo) para ver cómo lo identificó el analizador y lo que hizo.' }));
    for (const e of [0, 1]) {
      const suyos = [...rep.jugadores.values()].filter((j) => j.equipo === e).sort((a, b) => (b.portero - a.portero) || a.dorsal - b.dorsal);
      nodos.push(el('h2', {}, el('span', { clase: 'marca-equipo', estilo: { background: COLORES.equipo[e] } }), `${this._nombreEquipo(e)} · ${suyos.length}`));
      if (!suyos.length) nodos.push(el('div', { clase: 'sub', texto: 'Nadie identificado.' }));
      for (const j of suyos) {
        const resumen = rep.resumenes.get(j.id) || {};
        const metodo = j.por_descarte ? 'por descarte' : `dorsal ${Math.round(100 * (j.confianza || 0))} %`;
        const extra = [j.por_apariencia ? `+${j.por_apariencia} por aspecto` : null, j.por_continuidad ? `+${j.por_continuidad} por continuidad` : null].filter(Boolean).join(' · ');
        nodos.push(el('button', { clase: `jugador${seleccion === j.id ? ' elegido' : ''}`, type: 'button', onclick: () => this.elegir(j.id, true) },
          foto(rep, j),
          el('div', {},
            el('div', { clase: 'nombre', texto: j.nombre ? nombreCorto(j.nombre) : `Dorsal ${j.dorsal}` }),
            el('div', { clase: 'detalle', texto: [j.portero ? 'portero' : null, metodo, extra, resumen.segundos_a_la_vista != null ? `${duracion(resumen.segundos_a_la_vista)} a la vista` : null].filter(Boolean).join(' · ') }),
          ),
          el('span', { clase: 'dorsal', texto: j.dorsal }),
        ));
      }
    }
    const sin = rep.analisis.identificacion?.segundos_sin_identificar;
    if (sin) {
      nodos.push(el('h2', { texto: 'Sin identificar' }));
      nodos.push(el('div', { clase: 'sub', texto: `${this._nombreEquipo(0)}: ${duracion(sin['0'])} · ${this._nombreEquipo(1)}: ${duracion(sin['1'])} de tramos a la vista sin dorsal ni aspecto seguro (en el vídeo, con «?» y la caja discontinua).` }));
    }
    return nodos;
  }

  _ficha(id, t) {
    const rep = this.rep;
    const j = rep.jugadores.get(id);
    const resumen = rep.resumenes.get(id) || {};
    const acciones = rep.final.acciones[String(id)] || {};
    const equipo = j ? j.equipo : resumen.equipo;
    const titulo = j ? (j.nombre ? nombreCorto(j.nombre) : `Dorsal ${j.dorsal}`) : `Sin identificar #${id}`;
    const como = [];
    if (j) {
      como.push(j.por_descarte ? 'Dorsal por descarte (el acta y su equipación)' : `Dorsal leído: ${Math.round(100 * (j.confianza || 0))} % de sus lecturas dicen el ${j.dorsal}`);
      if (j.por_apariencia) como.push(`${j.por_apariencia} tramos más por su aspecto`);
      if (j.por_continuidad) como.push(`${j.por_continuidad} tramos más por continuidad`);
      como.push(`${j.tracklets} tramos del seguidor juntos`);
    }
    const momentos = Object.keys(TIPOS_ACCION).flatMap((tipo) => (acciones[tipo] || []).map((s) => ({ s, tipo }))).sort((a, b) => a.s - b.s);
    const siguiente = rep.siguienteAparicion(id, t);
    return el('div', { clase: 'ficha' },
      el('div', { clase: 'cabeza' },
        foto(rep, j, true),
        el('div', {},
          el('div', { clase: 'nombre' }, j ? `${j.dorsal} · ` : '', titulo),
          el('div', { clase: 'sub' }, el('span', { clase: 'marca-equipo', estilo: { background: COLORES.equipo[equipo] || COLORES.sinEquipo } }), equipo === 0 || equipo === 1 ? this._nombreEquipo(equipo) : 'Sin equipo', j?.portero ? ' · portero' : ''),
          el('div', { clase: 'filtros', estilo: { marginTop: '8px', marginBottom: '0' } },
            siguiente != null ? el('button', { clase: 'boton', type: 'button', onclick: () => this.ir(siguiente, id), texto: 'Ir a donde sale' }) : null,
            el('button', { clase: 'boton', type: 'button', onclick: () => this.elegir(null), texto: 'Quitar' }),
          ),
        ),
      ),
      como.length ? el('div', { clase: 'sub', estilo: { marginTop: '10px' } }, como.join(' · ')) : null,
      el('dl', { clase: 'datos', estilo: { marginTop: '10px' } },
        el('dt', { texto: 'A la vista' }), el('dd', { texto: duracion(resumen.segundos_a_la_vista) }),
        el('dt', { texto: 'Distancia mientras se le ve' }), el('dd', { texto: resumen.distancia_m != null ? `${numero(resumen.distancia_m)} m` : '–' }),
        el('dt', { texto: 'Velocidad máxima' }), el('dd', { texto: resumen.velocidad_max_kmh != null ? `${numero(resumen.velocidad_max_kmh, 1)} km/h` : '–' }),
        el('dt', { texto: 'Toques' }), el('dd', { texto: acciones.toques ?? '–' }),
        ...Object.keys(ACCIONES_PLURAL).flatMap((tipo) => [el('dt', { texto: ACCIONES_PLURAL[tipo] }), el('dd', { texto: (acciones[tipo] || []).length })]),
      ),
      momentos.length ? el('div', { clase: 'momentos' }, momentos.map((m) => el('button', { type: 'button', title: TIPOS_ACCION[m.tipo], onclick: () => this.ir(m.s - 2, id), texto: `${minuto(m.s)} ${TIPOS_ACCION[m.tipo]}` }))) : null,
      resumen.mapa_calor ? mapaCalor(resumen.mapa_calor, COLORES.equipo[equipo] || '#e2e8f0') : null,
    );
  }

  _eventos(t) {
    const rep = this.rep;
    const lista = [];
    if (this.filtro.jugadas) {
      for (const e of rep.eventos) {
        if (e.tipo === 'paron') continue;
        lista.push({ t: e.t, texto: `${TIPOS_EVENTO[e.tipo] || e.tipo}${e.equipo != null ? ' · ' + this._nombreEquipo(e.equipo) : ''}${e.tracklet != null ? ' · ' + this._quien(e.tracklet, e.equipo) : ''}`, id: e.tracklet, titulo: e.detalle });
      }
    }
    if (this.filtro.acciones) {
      for (const m of rep.momentos) lista.push({ t: m.t, texto: `${TIPOS_ACCION[m.tipo]} · ${this._quien(m.id, m.equipo)}`, id: m.id, color: COLORES.equipo[m.equipo] });
    }
    lista.sort((a, b) => a.t - b.t);
    let actual = -1;
    lista.forEach((x, i) => { if (x.t <= t) actual = i; });
    const chip = (clave, texto) => el('button', { clase: 'chip', type: 'button', 'aria-pressed': String(this.filtro[clave]), onclick: () => { this.filtro[clave] = !this.filtro[clave]; this.ultimaClave = null; this.actualizar(t); }, texto });
    return [
      el('div', { clase: 'filtros' }, chip('jugadas', 'Jugadas'), chip('acciones', 'Acciones que cuentan')),
      lista.length ? el('div', { clase: 'filas' }, lista.map((x, i) => fila(x, i === actual, x.t > t, () => this.ir(x.t - 2, x.id)))) : el('div', { clase: 'sub', texto: 'Nada que enseñar.' }),
    ];
  }

  _analisis() {
    const rep = this.rep;
    const a = rep.analisis;
    const r = a.recuento || {};
    const enJuego = r.balon_en_juego || {};
    const ident = a.identificacion || {};
    const cal = a.calibracion || {};
    const pos = a.posesion || {};
    const nodos = [];
    const datos = (pares) => el('dl', { clase: 'datos' }, pares.filter(Boolean).flatMap(([k, v]) => [el('dt', { texto: k }), el('dd', { texto: v ?? '–' })]));
    nodos.push(el('h2', { texto: 'Quién analizó' }));
    nodos.push(datos([
      ['Analizador', `${a.analizador?.nombre || 'Ojeador'} ${a.analizador?.version || a.version || ''}`],
      ['Analizado', rep.ficha.analizado ? new Date(rep.ficha.analizado).toLocaleString('es-ES', { dateStyle: 'medium', timeStyle: 'short' }) : '–'],
      ['Carpeta', rep.ficha.carpeta],
      ...(a.analizador?.modelos || []).map((m) => [m.que, [m.modelo, m.motor, m.huella].filter(Boolean).join(' · ')]),
    ]));
    nodos.push(el('h2', { texto: 'El vídeo' }));
    nodos.push(datos([
      ['Imagen', `${a.video?.ancho}×${a.video?.alto} a ${numero(a.video?.fps)} fps`],
      ['Tramo analizado', `${minuto(rep.inicio)} – ${minuto(rep.fin)}`],
      ['Fotogramas analizados', `${numero(r.fotogramas)} (${numero(a.parametros?.fps_muestreo, 1)} por segundo)`],
      ['Leído del archivo', r.pct_leido != null ? `${numero(r.pct_leido, 1)} %` : '–'],
      ['Cámara', a.parametros?.camara],
      ['Seguidor', a.parametros?.seguidor],
      ['Campo', a.parametros?.campo_m ? `${numero(a.parametros.campo_m[0])} × ${numero(a.parametros.campo_m[1])} m` : '–'],
    ]));
    nodos.push(el('h2', { texto: 'El balón' }));
    nodos.push(datos([
      ['Visto (en su fotograma o un vecino)', r.pct_con_balon != null ? `${numero(r.pct_con_balon, 1)} %` : '–'],
      ['Solo en su fotograma', r.pct_con_balon_fotograma != null ? `${numero(r.pct_con_balon_fotograma, 1)} %` : '–'],
      ['Con los huecos rellenados', r.pct_con_balon_total != null ? `${numero(r.pct_con_balon_total, 1)} %` : '–'],
      ['En juego', enJuego.pct_en_juego != null ? `${numero(enJuego.pct_en_juego, 1)} %` : '–'],
      ['Visto en juego', enJuego.pct_visto != null ? `${numero(enJuego.pct_visto, 1)} %` : '–'],
      ['Con trayectoria en juego', enJuego.pct_con_trayectoria != null ? `${numero(enJuego.pct_con_trayectoria, 1)} %` : '–'],
      ['Vecinos mirados / con balón', r.balon_vecinos ? `${numero(r.balon_vecinos.mirados)} / ${numero(r.balon_vecinos.detecciones)}` : '–'],
      ['Objetos quietos descartados', (rep.final.objetos_fijos || []).length],
    ]));
    nodos.push(el('h2', { texto: 'El campo' }));
    nodos.push(datos([
      ['Calibrado', r.pct_calibrados != null ? `${numero(r.pct_calibrados, 1)} %` : '–'],
      ['Modo', cal.modo],
      ['Calibraciones / intentos', cal.intentos != null ? `${numero(cal.aciertos)} / ${numero(cal.intentos)}` : '–'],
      ['Invalidadas', cal.invalidaciones],
      ['Mejor error', cal.error_px_mejor != null ? `${numero(cal.error_px_mejor, 1)} px` : '–'],
    ]));
    nodos.push(el('h2', { texto: 'Quién es quién' }));
    nodos.push(datos([
      ['Lecturas del dorsal', numero(ident.lecturas)],
      ['Junto al balón entre dos fotogramas', numero(ident.lecturas_junto_al_balon)],
      ['Tramos del seguidor', numero(r.tracklets)],
      ['Cadenas', numero(ident.cadenas)],
      ['Identificados', `${(ident.jugadores || []).filter((j) => j.equipo === 0).length} y ${(ident.jugadores || []).filter((j) => j.equipo === 1).length}`],
      ...Object.entries(ident.apariencia || {}).map(([e, x]) => [`Aspecto, ${this._nombreEquipo(Number(e))}`, x.umbral == null ? `no se usa (${x.probadas} probadas)` : `acierta ${x.aciertos}/${x.dadas} · da ${x.asignadas} cadenas`]),
    ]));
    nodos.push(el('h2', { texto: 'Posesión' }));
    nodos.push(datos([
      ...(pos.equipos || []).map((x) => [this._nombreEquipo(x.equipo), x.porcentaje != null ? `${numero(x.porcentaje, 1)} %` : '–']),
      ['Confianza', pos.confianza],
      ['Con dueño', pos.cobertura ? `${duracion(pos.cobertura.segundos_atribuidos)} de ${duracion(pos.cobertura.segundos_tramo)}` : '–'],
    ]));
    nodos.push(el('h2', { texto: 'Tiempos' }));
    nodos.push(datos(Object.entries(a.tiempos || {}).map(([k, v]) => [k.replace(/_s$/, '').replace(/_/g, ' '), duracion(v)])));
    nodos.push(el('h2', { texto: 'Todo' }));
    nodos.push(el('details', { clase: 'json' }, el('summary', { texto: 'analisis.json' }), el('pre', { texto: JSON.stringify(a, null, 1) })));
    if (rep.acta) nodos.push(el('details', { clase: 'json' }, el('summary', { texto: 'acta-propuesta.json (lo que recibe la app)' }), el('pre', { texto: JSON.stringify(rep.acta, null, 1) })));
    return nodos;
  }
}

function medida(nombre, valor, fraccion, color, explica) {
  return el('div', { clase: 'medida' },
    el('span', { texto: nombre }),
    el('span', { clase: 'valor', texto: valor }),
    el('div', { clase: 'regla' }, el('span', { estilo: { width: `${Math.max(0, Math.min(1, fraccion || 0)) * 100}%`, background: color } })),
    explica ? el('div', { clase: 'explica', texto: explica }) : null,
  );
}

function tablaEquipos(panel, filas) {
  return el('table', { clase: 'tabla' },
    el('thead', {}, el('tr', {},
      el('th', { texto: '' }),
      el('th', {}, el('span', { clase: 'marca-equipo', estilo: { background: COLORES.equipo[0] } }), el('span', { clase: 'equipo-nombre', texto: panel._nombreEquipo(0) })),
      el('th', {}, el('span', { clase: 'marca-equipo', estilo: { background: COLORES.equipo[1] } }), el('span', { clase: 'equipo-nombre', texto: panel._nombreEquipo(1) })),
    )),
    el('tbody', {}, filas.map(([nombre, a, b]) => el('tr', {}, el('td', { texto: nombre }), el('td', { texto: a }), el('td', { texto: b })))),
  );
}

function fila(x, actual, futura, onclick) {
  return el('button', { clase: `fila${actual ? ' actual' : ''}${futura ? ' futura' : ''}`, type: 'button', title: x.titulo || null, onclick },
    el('span', { clase: 'cuando', texto: minuto(x.t) }),
    el('span', { clase: 'que', texto: x.texto }),
  );
}

function foto(rep, j, grande = false) {
  if (j && j.recorte) {
    const nombre = j.recorte.split(/[\\/]/).pop();
    return el('img', { clase: grande ? null : 'foto', src: `/api/a/${rep.id}/recortes/${encodeURIComponent(nombre)}`, alt: '', loading: 'lazy' });
  }
  return el('div', { clase: grande ? 'sin-foto' : 'foto sin-foto', texto: j ? j.dorsal : '?' });
}

function mapaCalor(mapa, color) {
  const max = Math.max(1, ...mapa.flat());
  return el('div', { clase: 'calor', title: 'Dónde se le ve en el campo (12 × 8 casillas, de izquierda a derecha como en la imagen calibrada)' },
    mapa.flatMap((filaCeldas) => filaCeldas.map((v) => el('span', { estilo: { background: color, opacity: String(v ? 0.15 + 0.85 * (v / max) : 0) } })))
  );
}

function leyenda() {
  const item = (color, texto, discontinuo) => el('div', {}, el('span', { clase: 'color', estilo: { background: discontinuo ? 'transparent' : color, border: discontinuo ? `2px dashed ${color}` : 'none' } }), texto);
  return el('div', { clase: 'leyenda' },
    item(COLORES.equipo[0], 'Equipo A (color del visor, no el de su camiseta)'),
    item(COLORES.equipo[1], 'Equipo B'),
    item(COLORES.arbitro, 'Árbitro'),
    item('#cbd5e1', 'Caja discontinua: sin identificar («?»)', true),
    el('div', { texto: 'Etiqueta: dorsal y, si no se le leyó, cómo se le dio: A aspecto, C continuidad, D descarte.' }),
    item(COLORES.balon.v, `Balón ${ORIGEN_BALON.v}`),
    item(COLORES.balon.n, `Balón ${ORIGEN_BALON.n}`),
    item(COLORES.balon.i, `Balón ${ORIGEN_BALON.i} (círculo discontinuo)`),
    item(COLORES.balon.x, 'Balón descartado (aspa, con «Depuración»)'),
    el('div', { texto: 'Elipse bajo un jugador: tiene el balón (un toque). Líneas blancas: el campo como lo calibra el analizador.' }),
    el('div', { texto: `Métodos de identificación: ${Object.values(METODO).join(', ')}.` }),
  );
}
