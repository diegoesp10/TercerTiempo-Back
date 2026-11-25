// El visor del Ojeador: la lista de análisis y, de uno, el vídeo con lo que vio y sacó el analizador encima.

import { Repeticion, pedirJSON } from './datos.js';
import { Dibujo } from './dibujo.js';
import { Lineas } from './linea.js';
import { Panel } from './panel.js';
import { COLORES, ORIGEN_BALON, TIPOS_ACCION, TIPOS_EVENTO, buscarIndice, el, guardar, minuto, minutoPreciso, recordar } from './util.js';

const $ = (id) => document.getElementById(id);
const CAPAS = [
  ['jugadores', 'Jugadores', true, 'j'],
  ['etiquetas', 'Etiquetas', true, 'e'],
  ['balon', 'Balón', true, 'b'],
  ['estela', 'Estela', true, 't'],
  ['posesion', 'Posesión', true, 'p'],
  ['campo', 'Campo', true, 'c'],
  ['minimapa', 'Minimapa', true, 'm'],
  ['depuracion', 'Depuración', false, 'd'],
];

const BALON_CORTO = { v: 'Visto', n: 'Vecino', i: 'Interpolado', x: 'Descartado', '-': 'Sin balón' };

const video = $('video');
const capa = $('capa');
const minimapa = $('minimapa');
const capas = { ...Object.fromEntries(CAPAS.map(([k, , def]) => [k, def])), ...recordar('visor.capas', {}) };
const estado = { seleccion: null, desfase: 0, tMedia: 0, ultimoT: null, personas: [], escena: null, panelEn: 0, clavePildoras: '', avisoHasta: 0, guardadoEn: 0 };
let rep = null;
let dibujo = null;
let lineas = null;
let panel = null;
let abriendo = null;

// ---------------------------------------------------------------- arranque y lista

async function iniciar() {
  latir();
  montarControles();
  window.addEventListener('hashchange', enrutar);
  const inicio = await pedirJSON('/api/inicio').catch(() => ({}));
  if (!location.hash && inicio.analisis) location.hash = `#/a/${inicio.analisis}`;
  else enrutar();
}

function enrutar() {
  const m = location.hash.match(/^#\/a\/([0-9a-f]+)/);
  if (m) abrir(m[1]);
  else lista();
}

function latir() {
  const enviar = () => fetch('/api/latido', { method: 'POST', keepalive: true }).catch(() => {});
  enviar();
  setInterval(enviar, 20000);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) enviar(); });
  window.addEventListener('pagehide', () => navigator.sendBeacon('/api/adios'));
}

async function lista() {
  cerrarVisor();
  $('pantalla-visor').hidden = true;
  $('pantalla-lista').hidden = false;
  $('btn-lista').hidden = true;
  $('partido').textContent = '';
  $('version').textContent = '· visor';
  document.title = 'Ojeador · Visor';
  const cont = $('tarjetas');
  cont.replaceChildren(el('div', { clase: 'sub', texto: 'Buscando análisis…' }));
  try {
    const fichas = await pedirJSON('/api/analisis');
    cont.replaceChildren(...(fichas.length ? fichas.map(tarjeta) : [el('div', { clase: 'vacio', texto: 'No hay análisis en las carpetas del visor. Pega arriba la ruta de uno.' })]));
  } catch (e) {
    cont.replaceChildren(el('div', { clase: 'error', texto: e.message }));
  }
}

function tarjeta(f) {
  const titulo = f.equipos && f.equipos.length ? f.equipos.join(' – ') : f.carpeta.split(/[\\/]/).pop();
  const fecha = f.analizado ? new Date(f.analizado).toLocaleString('es-ES', { dateStyle: 'medium', timeStyle: 'short' }) : null;
  return el('button', { clase: 'tarjeta', type: 'button', onclick: () => { location.hash = `#/a/${f.id}`; } },
    el('span', { clase: 'titulo', texto: titulo }),
    el('span', { clase: 'linea-dato', texto: `Tramo ${minuto(f.tramo[0])} – ${minuto(f.tramo[1])} · ${f.analizador || 'Ojeador'}${fecha ? ' · ' + fecha : ''}` }),
    el('span', { clase: 'linea-dato', texto: f.carpeta }),
    el('span', { clase: 'etiquetas' },
      el('span', { clase: `etiqueta ${f.repeticion ? 'bien' : 'mal'}`, texto: f.repeticion ? 'Con repetición' : 'Sin repetición: vuelve a analizarlo' }),
      f.video_existe ? null : el('span', { clase: 'etiqueta mal', texto: 'No se encuentra el vídeo' }),
      f.balon_pct != null ? el('span', { clase: 'etiqueta', texto: `Balón visto ${String(f.balon_pct).replace('.', ',')} %` }) : null,
      f.jugadores ? el('span', { clase: 'etiqueta', texto: `${f.jugadores} identificados` }) : null,
    ),
  );
}

$('form-abrir').addEventListener('submit', async (e) => {
  e.preventDefault();
  const error = $('error-lista');
  error.hidden = true;
  try {
    const ficha = await pedirJSON('/api/abrir', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ carpeta: $('ruta-abrir').value }) });
    location.hash = `#/a/${ficha.id}`;
  } catch (err) {
    error.textContent = err.message;
    error.hidden = false;
  }
});
$('btn-lista').addEventListener('click', () => { location.hash = '#/'; });

// ---------------------------------------------------------------- abrir un análisis

function mensaje(...hijos) {
  const caja = $('mensaje-video');
  if (!hijos.length) { caja.hidden = true; caja.replaceChildren(); return; }
  caja.replaceChildren(el('div', { clase: 'caja' }, ...hijos));
  caja.hidden = false;
}

async function abrir(id) {
  if (rep && rep.id === id) return;
  cerrarVisor();
  $('pantalla-lista').hidden = true;
  $('pantalla-visor').hidden = false;
  $('btn-lista').hidden = false;
  mensaje(el('div', { texto: 'Cargando el análisis…' }));
  const esta = (abriendo = Symbol('abrir'));
  try {
    const ficha = await pedirJSON(`/api/a/${id}/ficha`);
    if (!ficha.repeticion) {
      mensaje(el('strong', { texto: 'Este análisis no tiene repetición.' }), el('div', { texto: 'Se hizo antes de que el Ojeador la guardara. Vuelve a analizar el vídeo (en la app, «Volver a analizar») y ábrelo otra vez.' }));
      return;
    }
    const [final, analisis, acta] = await Promise.all([
      pedirJSON(`/api/a/${id}/final.json`),
      pedirJSON(`/api/a/${id}/analisis.json`),
      pedirJSON(`/api/a/${id}/acta-propuesta.json`).catch(() => null),
    ]);
    if (esta !== abriendo) return;
    rep = new Repeticion(id, ficha, final, analisis, acta);
    dibujo = new Dibujo(rep);
    lineas = new Lineas($('linea-detalle'), $('linea-total'), rep, (t) => irA(t));
    panel = new Panel($('panel-cuerpo'), rep, { ir: (t, quien) => irA(t, quien), elegir: (quien, buscar) => elegir(quien, buscar) });
    rep.alCargar = () => { if (video.paused) repintar(); };
    estado.desfase = recordar(`visor.desfase.${id}`, 0);
    mostrarDesfase();
    const nombres = (final.equipos || []).map((e) => e.nombre).join(' – ');
    $('version').textContent = final.analizador?.version || '';
    $('partido').replaceChildren(el('strong', { texto: nombres }), ` · tramo ${minuto(rep.inicio)} – ${minuto(rep.fin)}`);
    document.title = `${nombres} · Ojeador`;
    marcarPestana(panel.pestana);
    const inicio = recordar(`visor.t.${id}`, rep.inicio);
    await rep.asegurar(inicio).catch(() => {});
    if (!ficha.video_existe) pedirVideo(ficha);
    else mensaje();
    video.src = `/api/a/${id}/video`;
    video.addEventListener('loadedmetadata', () => {
      video.currentTime = Math.min(Math.max(inicio, rep.inicio), rep.fin);
      redimensionar();
    }, { once: true });
    redimensionar();
    bucle();
  } catch (e) {
    mensaje(el('strong', { texto: 'No se ha podido abrir el análisis.' }), el('div', { texto: e.message }));
  }
}

function pedirVideo(ficha) {
  const entrada = el('input', { type: 'text', value: ficha.video || '', spellcheck: 'false', 'aria-label': 'Ruta del vídeo' });
  const error = el('div', { clase: 'error', hidden: true });
  mensaje(
    el('strong', { texto: 'No se encuentra el vídeo de este análisis.' }),
    el('div', { texto: 'Escribe dónde está (el mismo archivo que se analizó):' }),
    entrada,
    el('button', {
      clase: 'boton primario', type: 'button', texto: 'Usar este vídeo',
      onclick: async () => {
        try {
          await pedirJSON(`/api/a/${rep.id}/video`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ruta: entrada.value }) });
          mensaje();
          video.src = `/api/a/${rep.id}/video?${Date.now()}`;
          video.addEventListener('loadedmetadata', () => { video.currentTime = rep.inicio; redimensionar(); }, { once: true });
        } catch (e) {
          error.textContent = e.message;
          error.hidden = false;
        }
      },
    }),
    error,
  );
}

function cerrarVisor() {
  if (!rep) return;
  guardar(`visor.t.${rep.id}`, video.currentTime);
  video.pause();
  video.removeAttribute('src');
  video.load();
  rep = null;
  dibujo = lineas = panel = null;
  estado.seleccion = null;
  estado.clavePildoras = '';
  for (const lienzo of [capa, minimapa]) lienzo.getContext('2d').clearRect(0, 0, lienzo.width, lienzo.height);
  $('pildoras').replaceChildren();
}

// ---------------------------------------------------------------- dibujo

function bucle() {
  const propio = rep;
  if ('requestVideoFrameCallback' in HTMLVideoElement.prototype) {
    const alFotograma = (ahora, meta) => {
      if (rep !== propio) return;
      estado.tMedia = meta.mediaTime;
      pintar();
      video.requestVideoFrameCallback(alFotograma);
    };
    video.requestVideoFrameCallback(alFotograma);
  } else {
    const alCuadro = () => {
      if (rep !== propio) return;
      estado.tMedia = video.currentTime;
      pintar();
      requestAnimationFrame(alCuadro);
    };
    requestAnimationFrame(alCuadro);
  }
}

function repintar() {
  if (!rep) return;
  if (video.paused || video.seeking) estado.tMedia = video.currentTime;
  pintar(true);
}

function pintar(quieto = false) {
  if (!rep || !estado.escena) return;
  const t = estado.tMedia + estado.desfase;
  if (!rep.cargado(t)) rep.asegurar(t).catch(() => {});
  else rep.asegurar(t + 15).catch(() => {});
  const ctx = capa.getContext('2d');
  const { dpr } = estado.escena;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, capa.width, capa.height);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  estado.personas = dibujo.dibujar(ctx, estado.escena, t, capas, estado);
  minimapa.hidden = !capas.minimapa;
  if (capas.minimapa) {
    const mctx = minimapa.getContext('2d');
    mctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    dibujo.minimapa(mctx, minimapa.width / dpr, minimapa.height / dpr, t, estado.personas, estado);
  }
  pildoras(t);
  lineas.dibujar(t);
  if (!quieto) avisos(t);
  $('tiempo').replaceChildren(el('strong', { texto: minutoPreciso(t) }), ` / ${minuto(rep.fin)}`);
  const ahora = performance.now();
  if (quieto || ahora - estado.panelEn > 250) {
    estado.panelEn = ahora;
    panel.actualizar(t, estado.seleccion);
  }
  if (ahora - estado.guardadoEn > 5000) {
    estado.guardadoEn = ahora;
    guardar(`visor.t.${rep.id}`, video.currentTime);
  }
  estado.ultimoT = t;
}

function pildoras(t) {
  const k = rep.instante(t);
  let clave;
  let lista;
  // [texto, color, texto corto para cuando el vídeo es estrecho]
  if (t < rep.inicio - 0.1 || t > rep.fin + 0.1 || k < 0) {
    clave = 'fuera';
    lista = [['Fuera del tramo analizado', '#64748b', 'Fuera del tramo']];
  } else {
    const b = rep.balon[k];
    const q = rep.posesion[k];
    const conEquipo = q === '0' || q === '1';
    clave = `${b}${rep.enJuego[k]}${rep.calibrado[k]}${q}`;
    lista = [
      rep.enJuego[k] === '1' ? ['En juego', '#e2e8f0', 'En juego'] : ['Parado', '#64748b', 'Parado'],
      [b === '-' ? 'Sin balón' : `Balón ${ORIGEN_BALON[b]}`, COLORES.balon[b] || '#64748b', BALON_CORTO[b]],
      conEquipo ? [`Posesión: ${rep.equipos[Number(q)].nombre}`, COLORES.equipo[Number(q)], rep.equipos[Number(q)].nombre] : ['Posesión: a nadie', '#64748b', 'Sin posesión'],
      rep.calibrado[k] === '1' ? ['Campo calibrado', COLORES.calibrado, 'Calibrado'] : ['Sin calibrar', '#64748b', 'Sin calibrar'],
    ];
  }
  if (clave === estado.clavePildoras) return;
  estado.clavePildoras = clave;
  $('pildoras').replaceChildren(...lista.map(([texto, color, corto]) => el('span', { clase: 'pildora' },
    el('span', { clase: 'punto', estilo: { background: color } }),
    el('span', { clase: 'largo', texto }),
    el('span', { clase: 'corto', texto: corto || texto }),
  )));
}

// Al pasar por una acción que cuenta o una jugada mientras se reproduce, un aviso arriba.
function avisos(t) {
  const caja = $('aviso-evento');
  const antes = estado.ultimoT;
  if (antes != null && !video.paused && t > antes && t - antes < 1.5) {
    const pasadas = [];
    for (let i = buscarIndice(rep.momentosT, antes) + 1; i < rep.momentos.length && rep.momentos[i].t <= t; i++) {
      const m = rep.momentos[i];
      pasadas.push({ texto: `${TIPOS_ACCION[m.tipo]} · ${quien(m.id, m.equipo)}`, color: COLORES.equipo[m.equipo] });
    }
    for (let i = buscarIndice(rep.eventosT, antes) + 1; i < rep.eventos.length && rep.eventos[i].t <= t; i++) {
      const e = rep.eventos[i];
      if (e.tipo === 'paron') continue;
      pasadas.push({ texto: `${TIPOS_EVENTO[e.tipo] || e.tipo}${e.equipo != null ? ' · ' + rep.equipos[e.equipo].nombre : ''}`, color: COLORES.equipo[e.equipo] || '#e2e8f0' });
    }
    if (pasadas.length) {
      const ultimo = pasadas[pasadas.length - 1];
      caja.textContent = ultimo.texto;
      caja.style.borderColor = ultimo.color || '#ffffff2a';
      caja.classList.add('visible');
      estado.avisoHasta = performance.now() + 2200;
    }
  }
  if (estado.avisoHasta && performance.now() > estado.avisoHasta) {
    caja.classList.remove('visible');
    estado.avisoHasta = 0;
  }
}

function quien(id, equipo) {
  const j = rep.jugadores.get(id);
  if (j) return rep.nombreDe(id);
  return equipo === 0 || equipo === 1 ? `sin identificar (${rep.equipos[equipo].nombre})` : 'sin identificar';
}

function redimensionar() {
  if (!rep) return;
  const caja = $('video-caja');
  const dpr = window.devicePixelRatio || 1;
  const w = caja.clientWidth, h = caja.clientHeight;
  capa.width = Math.max(1, Math.round(w * dpr));
  capa.height = Math.max(1, Math.round(h * dpr));
  const vw = video.videoWidth || rep.video.ancho;
  const vh = video.videoHeight || rep.video.alto;
  const encaje = Math.min(w / vw, h / vh);
  const anchoVisto = vw * encaje, altoVisto = vh * encaje;
  const ox = (w - anchoVisto) / 2, oy = (h - altoVisto) / 2;
  // Lo que da el analizador está en píxeles del vídeo que analizó.
  estado.escena = { escala: anchoVisto / rep.video.ancho, ox, oy, dpr };
  const mw = Math.round(Math.min(340, Math.max(anchoVisto * 0.27, Math.min(120, anchoVisto * 0.34))));
  const mh = Math.round((mw - 16) * rep.campo.ancho_cm / rep.campo.largo_cm + 16);
  minimapa.style.width = `${mw}px`;
  minimapa.style.height = `${mh}px`;
  minimapa.width = Math.round(mw * dpr);
  minimapa.height = Math.round(mh * dpr);
  minimapa.style.left = `${ox + anchoVisto - mw - 10}px`;
  minimapa.style.top = `${oy + altoVisto - mh - 10}px`;
  lineas.redimensionar();
  repintar();
}

new ResizeObserver(() => redimensionar()).observe($('video-caja'));
window.addEventListener('resize', () => redimensionar());

// ---------------------------------------------------------------- controles

function montarControles() {
  const cont = $('capas');
  for (const [clave, nombre, , tecla] of CAPAS) {
    cont.append(el('button', {
      clase: 'chip', type: 'button', 'data-capa': clave, 'aria-pressed': String(!!capas[clave]), title: `${nombre} (${tecla.toUpperCase()})`, texto: nombre,
      onclick: () => alternarCapa(clave),
    }));
  }
  $('btn-play').addEventListener('click', alternarPlay);
  $('btn-atras').addEventListener('click', () => saltar(-5));
  $('btn-adelante').addEventListener('click', () => saltar(5));
  $('btn-fot-atras').addEventListener('click', () => instanteVecino(-1));
  $('btn-fot-adelante').addEventListener('click', () => instanteVecino(1));
  $('velocidad').addEventListener('change', (e) => { video.playbackRate = Number(e.target.value); });
  video.addEventListener('play', () => { $('btn-play').textContent = '❚❚'; $('btn-play').setAttribute('aria-label', 'Pausar'); });
  video.addEventListener('pause', () => { $('btn-play').textContent = '▶'; $('btn-play').setAttribute('aria-label', 'Reproducir'); if (rep) guardar(`visor.t.${rep.id}`, video.currentTime); repintar(); });
  video.addEventListener('seeked', () => repintar());
  video.addEventListener('error', () => {
    if (rep && video.getAttribute('src')) mensaje(el('strong', { texto: 'El navegador no puede reproducir este vídeo.' }), el('div', { texto: 'Puede que el archivo no exista o que su formato no lo lea Edge (el que deja la app, H.264 en MP4, sí).' }));
  });
  for (const boton of document.querySelectorAll('.pestana')) {
    boton.addEventListener('click', () => {
      if (!panel) return;
      marcarPestana(boton.dataset.pestana);
      panel.mostrar(boton.dataset.pestana);
      panel.actualizar(estado.tMedia + estado.desfase, estado.seleccion);
    });
  }
  capa.addEventListener('pointermove', (e) => {
    const p = personaEn(e);
    capa.classList.toggle('sobre-jugador', !!p);
    capa.title = p ? descripcion(p) : '';
  });
  capa.addEventListener('click', (e) => {
    const p = personaEn(e);
    if (p) elegir(estado.seleccion === p.id ? null : p.id);
    else alternarPlay();
  });
  document.addEventListener('keydown', teclado);
}

function marcarPestana(nombre) {
  for (const boton of document.querySelectorAll('.pestana')) boton.setAttribute('aria-selected', String(boton.dataset.pestana === nombre));
}

function alternarCapa(clave) {
  capas[clave] = !capas[clave];
  guardar('visor.capas', capas);
  const chip = document.querySelector(`[data-capa="${clave}"]`);
  if (chip) chip.setAttribute('aria-pressed', String(capas[clave]));
  repintar();
}

function alternarPlay() {
  if (!rep) return;
  if (video.paused) video.play().catch(() => {});
  else video.pause();
}

function saltar(segundos) {
  if (!rep) return;
  irA(video.currentTime + estado.desfase + segundos);
}

function irA(t, quien) {
  if (!rep) return;
  const destino = Math.min(Math.max(t, rep.inicio), rep.fin) - estado.desfase;
  rep.asegurar(destino + estado.desfase).catch(() => {});
  video.currentTime = Math.max(0, destino);
  if (quien != null && (rep.jugadores.has(quien) || rep.resumenes.has(quien))) elegir(quien);
  repintar();
}

// Al instante analizado anterior o siguiente (donde el analizador miró de verdad).
function instanteVecino(sentido) {
  if (!rep) return;
  video.pause();
  const t = video.currentTime + estado.desfase;
  const k = rep.instante(t + 1e-3);
  let destino;
  if (sentido > 0) destino = rep.t[Math.min(rep.t.length - 1, k + 1)];
  else destino = rep.t[Math.max(0, t - rep.t[Math.max(0, k)] > 0.02 ? k : k - 1)];
  if (destino != null) irA(destino);
}

function elegir(id, buscar = false) {
  estado.seleccion = id;
  if (id != null && panel) {
    marcarPestana('jugadores');
    panel.mostrar('jugadores');
  }
  if (buscar && id != null) {
    const t = video.currentTime + estado.desfase;
    const visible = estado.personas.some((p) => p.id === id);
    if (!visible) {
      const siguiente = rep.siguienteAparicion(id, t) ?? rep.resumenes.get(id)?.desde ?? null;
      if (siguiente != null) irA(siguiente);
    }
  }
  repintar();
}

function personaEn(e) {
  if (!estado.escena || !estado.personas.length) return null;
  const r = capa.getBoundingClientRect();
  const { escala, ox, oy } = estado.escena;
  const x = (e.clientX - r.left - ox) / escala;
  const y = (e.clientY - r.top - oy) / escala;
  let mejor = null;
  let area = Infinity;
  for (const p of estado.personas) {
    const [x1, y1, x2, y2] = p.caja;
    const margen = 4 / escala;
    if (x >= x1 - margen && x <= x2 + margen && y >= y1 - 22 / escala && y <= y2 + margen) {
      const a = (x2 - x1) * (y2 - y1);
      if (a < area) { area = a; mejor = p; }
    }
  }
  return mejor;
}

function descripcion(p) {
  const j = rep.jugadores.get(p.id);
  const info = rep.tracklets.get(p.tracklet);
  const partes = [];
  if (p.clase === 3) partes.push('Árbitro');
  else if (j) partes.push(`${rep.nombreDe(p.id)}${j.portero ? ' (portero)' : ''}`);
  else partes.push('Sin identificar');
  if (p.equipo === 0 || p.equipo === 1) partes.push(rep.equipos[p.equipo].nombre);
  if (info && info.metodo) partes.push({ dorsal: 'por el dorsal leído', aspecto: 'por el aspecto', continuidad: 'por continuidad', descarte: 'por descarte' }[info.metodo]);
  partes.push(`tramo #${p.tracklet}${p.seguidor !== p.tracklet ? ` (del seguidor #${p.seguidor})` : ''}${info ? `, cadena ${info.cadena}` : ''}`);
  return partes.join(' · ');
}

function mostrarDesfase() {
  const nodo = $('desfase');
  nodo.hidden = !estado.desfase;
  if (rep && estado.desfase) nodo.textContent = `Desfase ${estado.desfase > 0 ? '+' : ''}${Math.round(estado.desfase * rep.video.fps)} fot.`;
}

function teclado(e) {
  if (!rep || e.target.closest('input, select, textarea')) return;
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  const tecla = e.key;
  if (tecla === ' ') { e.preventDefault(); alternarPlay(); return; }
  if (tecla === 'ArrowLeft') { e.preventDefault(); saltar(e.shiftKey ? -5 : -1); return; }
  if (tecla === 'ArrowRight') { e.preventDefault(); saltar(e.shiftKey ? 5 : 1); return; }
  if (tecla === ',') { instanteVecino(-1); return; }
  if (tecla === '.') { instanteVecino(1); return; }
  if (tecla === 'Escape') { elegir(null); return; }
  if (tecla === '[' || tecla === ']' || tecla === '0') {
    // Por si el vídeo y lo dibujado no van a la par: un fotograma de vídeo cada vez.
    estado.desfase = tecla === '0' ? 0 : estado.desfase + (tecla === ']' ? 1 : -1) / rep.video.fps;
    guardar(`visor.desfase.${rep.id}`, estado.desfase);
    mostrarDesfase();
    repintar();
    return;
  }
  const capaTecla = CAPAS.find(([, , , t]) => t === tecla.toLowerCase());
  if (capaTecla) alternarCapa(capaTecla[0]);
}

iniciar();
