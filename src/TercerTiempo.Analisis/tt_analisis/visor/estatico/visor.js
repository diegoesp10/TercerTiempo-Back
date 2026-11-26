// El visor del Ojeador: la lista de análisis y, de uno, el vídeo con lo que vio y sacó el analizador encima. Un análisis
// que se está haciendo se abre en directo: se sigue lo provisional según avanza y, al terminar, se pasa solo a la
// repetición definitiva sin tocar el vídeo.

import { Repeticion, pedirJSON } from './datos.js';
import { Dibujo } from './dibujo.js';
import { Lineas } from './linea.js';
import { Panel } from './panel.js';
import { COLORES, ETAPAS, ORIGEN_BALON, TIPOS_ACCION, TIPOS_EVENTO, buscarIndice, el, estadoEnCurso, guardar, minuto, minutoPreciso, recordar } from './util.js';

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
// Cada cuánto se mira cómo va un análisis en marcha (él escribe cada instante y su estado cada 2 s) y la lista.
const SEGUIR_MS = 1500;
const LISTA_MS = 4000;
// En directo, al reproducir, el vídeo espera a este margen de donde va el análisis y sigue cuando hay de nuevo este otro.
const MARGEN_ESPERA_S = 0.4;
const MARGEN_SEGUIR_S = 3;

const video = $('video');
const capa = $('capa');
const minimapa = $('minimapa');
const capas = { ...Object.fromEntries(CAPAS.map(([k, , def]) => [k, def])), ...recordar('visor.capas', {}) };
const estado = { seleccion: null, desfase: 0, tMedia: 0, ultimoT: null, personas: [], escena: null, panelEn: 0, clavePildoras: '', avisoHasta: 0, guardadoEn: 0, esperando: false };
let rep = null;
let dibujo = null;
let lineas = null;
let panel = null;
let abriendo = null;
let seguimiento = null;
let refrescoLista = null;
const tarjetasHechas = new Map();

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
  tarjetasHechas.clear();
  cont.replaceChildren(el('div', { clase: 'sub', texto: 'Buscando análisis…' }));
  pintarCarpetas();
  await refrescarLista();
}

// La lista se vuelve a pedir cada poco mientras se ve: así salen los análisis que empiezan y cómo van los que están en marcha.
async function refrescarLista() {
  clearTimeout(refrescoLista);
  if ($('pantalla-lista').hidden) return;
  const cont = $('tarjetas');
  try {
    const fichas = await pedirJSON('/api/analisis');
    if ($('pantalla-lista').hidden) return;
    if (!fichas.length) {
      tarjetasHechas.clear();
      cont.replaceChildren(el('div', { clase: 'vacio', texto: 'No hay análisis en las carpetas del visor. Pega arriba la ruta de uno.' }));
    } else {
      // Solo se rehace la tarjeta que ha cambiado (para no quitarle el clic a la que se está pulsando).
      const nodos = fichas.map((f) => {
        const clave = JSON.stringify(f);
        const hecha = tarjetasHechas.get(f.id);
        if (hecha && hecha.clave === clave) return hecha.nodo;
        const nodo = tarjeta(f);
        tarjetasHechas.set(f.id, { clave, nodo });
        return nodo;
      });
      const cambia = nodos.length !== cont.children.length || nodos.some((n, i) => cont.children[i] !== n);
      if (cambia) cont.replaceChildren(...nodos);
    }
  } catch (e) {
    cont.replaceChildren(el('div', { clase: 'error', texto: e.message }));
  }
  refrescoLista = setTimeout(refrescarLista, LISTA_MS);
}

function tarjeta(f) {
  const titulo = f.equipos && f.equipos.length ? f.equipos.join(' – ') : f.carpeta.split(/[\\/]/).pop();
  const fecha = f.analizado ? new Date(f.analizado).toLocaleString('es-ES', { dateStyle: 'medium', timeStyle: 'short' }) : null;
  const marcha = f.en_curso;
  let principal;
  if (marcha) {
    const que = estadoEnCurso(marcha);
    principal = el('span', { clase: `etiqueta ${que.clase}`, title: marcha.error || null }, que.clase === 'directo' ? el('span', { clase: 'punto-vivo', 'aria-hidden': 'true' }) : null, que.texto);
  } else {
    principal = el('span', { clase: `etiqueta ${f.repeticion ? 'bien' : 'mal'}`, texto: f.repeticion ? 'Con repetición' : 'Sin repetición: vuelve a analizarlo' });
  }
  const analizando = !!(marcha && marcha.vivo);
  return el('div', { clase: `tarjeta${marcha ? ' en-curso' : ''}` },
    el('button', { clase: 'tarjeta-abrir', type: 'button', onclick: () => { location.hash = `#/a/${f.id}`; } },
      el('span', { clase: 'titulo', texto: titulo }),
      el('span', { clase: 'linea-dato', texto: `Tramo ${minuto(f.tramo[0])} – ${minuto(f.tramo[1])} · ${f.analizador || 'Ojeador'}${fecha ? (marcha ? ' · empezado ' : ' · ') + fecha : ''}` }),
      el('span', { clase: 'linea-dato', texto: f.carpeta }),
      el('span', { clase: 'etiquetas' },
        principal,
        f.de_la_app ? el('span', { clase: 'etiqueta', texto: 'De la app' }) : null,
        f.video_existe ? null : el('span', { clase: 'etiqueta mal', texto: 'No se encuentra el vídeo' }),
        !marcha && f.balon_pct != null ? el('span', { clase: 'etiqueta', texto: `Balón visto ${String(f.balon_pct).replace('.', ',')} %` }) : null,
        !marcha && f.jugadores ? el('span', { clase: 'etiqueta', texto: `${f.jugadores} identificados` }) : null,
      ),
    ),
    el('div', { clase: 'tarjeta-acciones' },
      el('button', { clase: 'boton pequeno', type: 'button', title: 'Abrir su carpeta en el Explorador', onclick: () => explorar(f.carpeta), texto: 'Carpeta' }),
      el('button', {
        clase: 'boton pequeno peligro', type: 'button', disabled: analizando,
        title: analizando ? 'Se está analizando: se podrá borrar cuando termine' : 'Borrar este análisis',
        onclick: () => confirmarBorrado(f), texto: 'Borrar',
      }),
    ),
  );
}

// ---------------------------------------------------------------- dónde se guardan, abrir su carpeta y borrar

function queCarpeta(ruta) {
  const r = ruta.toLowerCase().replace(/\//g, '\\').replace(/\\+$/, '');
  if (r.endsWith('\\videos\\analysis')) return 'Los de la app (disco de Docker)';
  if (r.endsWith('\\salidas')) return 'Los que lanzas a mano';
  return 'Otra carpeta';
}

async function pintarCarpetas() {
  const cont = $('carpetas');
  try {
    const carpetas = await pedirJSON('/api/carpetas');
    cont.replaceChildren(
      el('h2', { texto: 'Dónde se guardan' }),
      ...carpetas.map((c) => el('div', { clase: 'carpeta-fila' },
        el('span', { clase: 'que', texto: queCarpeta(c.ruta) }),
        el('span', { clase: 'ruta', texto: c.ruta }),
        el('button', {
          clase: 'boton pequeno', type: 'button', disabled: !c.existe, onclick: () => explorar(c.ruta), texto: 'Abrir',
          title: c.existe ? 'Abrir en el Explorador' : 'Esta carpeta aún no existe',
        }),
      )),
    );
    cont.hidden = !carpetas.length;
  } catch {
    cont.hidden = true;
  }
}

async function explorar(carpeta) {
  try {
    await pedirJSON('/api/explorar', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ carpeta }) });
  } catch (e) {
    notificar(e.message, true);
  }
}

let ocultarAviso = null;
function avisoLista(texto, mal = false) {
  const caja = $('aviso-lista');
  caja.textContent = texto;
  caja.classList.toggle('mal', mal);
  caja.hidden = false;
  clearTimeout(ocultarAviso);
  ocultarAviso = setTimeout(() => { caja.hidden = true; }, 6000);
}

function notificar(texto, mal = false) {
  if ($('pantalla-lista').hidden) avisar(texto, mal ? COLORES.balon.x : COLORES.balon.v, 5000);
  else avisoLista(texto, mal);
}

function confirmarBorrado(f) {
  const dialogo = $('dialogo-borrar');
  const titulo = f.equipos && f.equipos.length ? f.equipos.join(' – ') : f.carpeta.split(/[\\/]/).pop();
  $('borrar-partido').textContent = `${titulo} · tramo ${minuto(f.tramo[0])} – ${minuto(f.tramo[1])}`;
  $('borrar-ruta').textContent = f.carpeta;
  $('borrar-como').textContent = f.papelera
    ? 'Se borra su carpeta con todo lo que dejó el análisis (la repetición, la propuesta, las fotos y los mapas) y va a la Papelera de Windows, de donde se puede recuperar. El vídeo no se toca.'
    : 'Se borra su carpeta con todo lo que dejó el análisis (la repetición, la propuesta, las fotos y los mapas). Está en un disco sin Papelera (el de Docker o uno de red), así que se borra del todo y no se puede recuperar. El vídeo no se toca.';
  $('borrar-app').hidden = !f.de_la_app;
  const error = $('borrar-error');
  error.hidden = true;
  const si = $('btn-borrar-si');
  si.disabled = false;
  si.textContent = f.papelera ? 'Mandar a la Papelera' : 'Borrar del todo';
  si.onclick = async () => {
    si.disabled = true;
    error.hidden = true;
    try {
      const r = await pedirJSON(`/api/a/${f.id}/borrar`, { method: 'POST' });
      dialogo.close();
      tarjetasHechas.delete(f.id);
      // Si se estaba viendo, a la lista (donde sale el aviso).
      if (rep && rep.id === f.id) location.hash = '#/';
      else refrescarLista();
      avisoLista(r.papelera ? 'Análisis borrado: está en la Papelera de Windows.' : 'Análisis borrado.');
    } catch (e) {
      error.textContent = e.message;
      error.hidden = false;
      // Si ya no está, se está analizando o su carpeta tiene más cosas, volver a pulsar no cambia nada.
      const definitivo = e.estado === 404 || e.estado === 409;
      si.disabled = definitivo;
      if (definitivo) refrescarLista();
    }
  };
  dialogo.showModal();
}

$('btn-borrar-no').addEventListener('click', () => $('dialogo-borrar').close());

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
  clearTimeout(refrescoLista);
  $('pantalla-lista').hidden = true;
  $('pantalla-visor').hidden = false;
  $('btn-lista').hidden = false;
  mensaje(el('div', { texto: 'Cargando el análisis…' }));
  const esta = (abriendo = Symbol('abrir'));
  try {
    const ficha = await pedirJSON(`/api/a/${id}/ficha`);
    if (esta !== abriendo) return;
    let nueva;
    if (ficha.en_curso) nueva = await enMarcha(id, ficha, esta);
    else if (ficha.repeticion) nueva = await definitiva(id, ficha);
    else {
      mensaje(el('strong', { texto: 'Este análisis no tiene repetición.' }), el('div', { texto: 'Se hizo antes de que el Ojeador la guardara. Vuelve a analizar el vídeo (en la app, «Volver a analizar») y ábrelo otra vez.' }));
      return;
    }
    if (!nueva || esta !== abriendo) return;
    await montar(nueva, false);
  } catch (e) {
    if (esta === abriendo) mensaje(el('strong', { texto: 'No se ha podido abrir el análisis.' }), el('div', { texto: e.message }));
  }
}

async function definitiva(id, ficha) {
  const [final, analisis, acta] = await Promise.all([
    pedirJSON(`/api/a/${id}/final.json`),
    pedirJSON(`/api/a/${id}/analisis.json`),
    pedirJSON(`/api/a/${id}/acta-propuesta.json`).catch(() => null),
  ]);
  return new Repeticion(id, ficha, final, analisis, acta);
}

// Un análisis en marcha. Hasta que empieza la pasada principal (cargar los modelos y separar los equipos) no hay nada que
// dibujar: se espera enseñando cómo va.
async function enMarcha(id, ficha, esta) {
  for (;;) {
    const d = await pedirJSON(`/api/a/${id}/directo?desde=0`);
    if (esta !== abriendo) return null;
    if (d.terminado) return definitiva(id, await pedirJSON(`/api/a/${id}/ficha`));
    if (d.campo && d.equipos && d.dt) return Repeticion.enMarcha(id, ficha, d);
    if (d.sin_directo) {
      mensaje(el('strong', { texto: 'Ya no llega nada en directo de este análisis.' }), el('div', { texto: 'Dejó de escribir lo provisional, o se vació su carpeta. Si termina, se verá aquí.' }));
    } else {
      const que = estadoEnCurso({ ...d, fotogramas: null });
      const hitos = (d.hitos || []).slice(-4);
      mensaje(
        el('strong', { texto: d.error ? 'El análisis ha fallado antes de empezar a seguir el partido.' : que.parado ? 'El análisis se ha parado antes de empezar a seguir el partido.' : 'El análisis está empezando.' }),
        el('div', { texto: d.error || `${ETAPAS[d.etapa] || 'Preparando'}… En cuanto empiece a seguir a jugadores y balón se verá aquí, según avanza.` }),
        hitos.length ? el('div', { clase: 'hitos-mensaje' }, hitos.map((h) => el('div', { texto: h }))) : null,
      );
      if (d.error || que.parado) return null;
    }
    await new Promise((resolver) => setTimeout(resolver, SEGUIR_MS));
    if (esta !== abriendo) return null;
  }
}

// Pone en pantalla una repetición. Con `conservar` (el análisis en marcha acaba de terminar), el vídeo sigue donde estaba.
async function montar(nueva, conservar) {
  const pestana = panel ? panel.pestana : null;
  clearTimeout(seguimiento);
  rep = nueva;
  dibujo = new Dibujo(rep);
  lineas = new Lineas($('linea-detalle'), $('linea-total'), rep, (t) => irA(t));
  panel = new Panel($('panel-cuerpo'), rep, {
    ir: (t, quien) => irA(t, quien), elegir: (quien, buscar) => elegir(quien, buscar), loUltimo: () => irALoUltimo(),
    carpeta: () => explorar(rep.ficha.carpeta), borrar: () => confirmarBorrado(rep.ficha),
  });
  if (pestana) panel.mostrar(pestana);
  rep.alCargar = () => { if (video.paused) repintar(); };
  estado.desfase = recordar(`visor.desfase.${rep.id}`, 0);
  estado.clavePildoras = '';
  estado.esperando = false;
  mostrarDesfase();
  cabecera();
  marcarPestana(panel.pestana);
  $('btn-directo').hidden = !rep.enDirecto;
  if (conservar) {
    rep.asegurar(video.currentTime + estado.desfase).catch(() => {});
  } else {
    // En directo, de entrada, donde se quedó si ya está analizado; si no, lo último que ha visto el analizador.
    let inicio = recordar(`visor.t.${rep.id}`, rep.inicio);
    if (rep.enDirecto && (inicio > rep.frontera() || inicio < rep.inicio)) inicio = Math.max(rep.inicio, rep.frontera() - 8);
    await rep.asegurar(inicio).catch(() => {});
    if (!rep.ficha.video_existe) pedirVideo(rep.ficha);
    else mensaje();
    silenciar();
    video.src = `/api/a/${rep.id}/video`;
    video.addEventListener('loadedmetadata', () => {
      video.currentTime = Math.min(Math.max(inicio, rep.inicio), rep.fin);
      redimensionar();
    }, { once: true });
  }
  redimensionar();
  bucle();
  if (rep.enDirecto) seguir();
}

function cabecera() {
  const nombres = (rep.equipos || []).map((e) => e.nombre).join(' – ');
  $('version').textContent = rep.final.analizador?.version || '';
  $('partido').replaceChildren(...[
    el('strong', { texto: nombres }), ` · tramo ${minuto(rep.inicio)} – ${minuto(rep.fin)}`,
    rep.enDirecto ? el('span', { clase: 'en-directo' }, el('span', { clase: 'punto-vivo', 'aria-hidden': 'true' }), 'en directo') : null,
  ].filter(Boolean));
  document.title = `${rep.enDirecto ? '● ' : ''}${nombres} · Ojeador`;
}

// Lo nuevo de un análisis en marcha, cada SEGUIR_MS: sus instantes y su estado. Al aparecer la repetición definitiva se
// pasa a ella; si en la carpeta empieza otro análisis, se abre ese.
function seguir() {
  const propio = rep;
  const paso = async () => {
    if (rep !== propio) return;
    try {
      const d = await pedirJSON(`/api/a/${propio.id}/directo?desde=${propio.t.length}`);
      if (rep !== propio) return;
      if (d.terminado) {
        await terminado(propio, d.parado);
        return;
      }
      if (d.sin_directo) {
        // Ni lo provisional ni la definitiva (el analizador dejó de escribir lo de en directo, o se vació la carpeta): se
        // queda lo que había y se sigue mirando, por si termina o empieza otro.
        if (!propio.directo.sin_directo) {
          propio.crecer({ ...propio.directo, sin_directo: true, instantes: null });
          avisar('Ya no llega nada en directo de este análisis: si termina, se verá aquí.', COLORES.balon.i, 8000);
        }
      } else if (d.inicio !== propio.directo.inicio) {
        reabrir(propio.id);
        return;
      } else if (propio.crecer(d)) {
        lineas.actualizar();
      }
      estado.clavePildoras = '';
      if (estado.esperando) {
        // Sigue cuando hay más analizado; si la pasada ya acabó, no va a haber más: sigue con lo que queda. Si el
        // análisis se ha parado, deja de esperar y el vídeo queda en pausa.
        const parado = estadoEnCurso(propio.directo).parado;
        const sinMas = parado || propio.directo.etapa !== 'pasada';
        if (sinMas || propio.frontera() >= video.currentTime + estado.desfase + MARGEN_SEGUIR_S) {
          estado.esperando = false;
          if (!parado) video.play().catch(() => {});
        }
      }
      if (video.paused) repintar();
    } catch {
      // El siguiente intento.
    }
    if (rep === propio) seguimiento = setTimeout(paso, SEGUIR_MS);
  };
  seguimiento = setTimeout(paso, SEGUIR_MS);
}

// Con `parado`, la repetición que hay es la de un análisis anterior en la misma carpeta: el que se seguía no terminó.
async function terminado(propio, parado) {
  try {
    const ficha = await pedirJSON(`/api/a/${propio.id}/ficha`);
    if (rep !== propio) return;
    if (!ficha.repeticion) {
      reabrir(propio.id);
      return;
    }
    const nueva = await definitiva(propio.id, ficha);
    if (rep !== propio) return;
    // Lo elegido en directo es un tramo del seguidor; en la definitiva, los ids son otros.
    estado.seleccion = null;
    await montar(nueva, true);
    if (parado) avisar(`El análisis se ha parado sin terminar (${parado}): se ve el anterior, el que ya había en la carpeta.`, COLORES.balon.x, 9000);
    else avisar('Análisis terminado: ya está todo (quién es quién, posesión, jugadas y lo que cuenta para el acta).', COLORES.balon.v, 5000);
  } catch {
    if (rep === propio) seguimiento = setTimeout(() => terminado(propio, parado), SEGUIR_MS);
  }
}

function reabrir(id) {
  cerrarVisor();
  abrir(id);
}

function irALoUltimo() {
  if (!rep || !rep.enDirecto) return;
  irA(Math.max(rep.inicio, rep.frontera() - 5));
  video.play().catch(() => {});
}

function avisar(texto, color, ms = 2200) {
  const caja = $('aviso-evento');
  caja.textContent = texto;
  caja.style.borderColor = color || '#ffffff2a';
  caja.classList.add('visible', 'largo');
  estado.avisoHasta = performance.now() + ms;
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
  clearTimeout(seguimiento);
  abriendo = null;
  if (!rep) return;
  guardar(`visor.t.${rep.id}`, video.currentTime);
  video.pause();
  video.removeAttribute('src');
  video.load();
  rep = null;
  dibujo = lineas = panel = null;
  estado.seleccion = null;
  estado.clavePildoras = '';
  estado.esperando = false;
  $('btn-directo').hidden = true;
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
  // En directo, el vídeo que llega a donde va el análisis lo espera (y sigue solo cuando hay más).
  if (rep.enDirecto && rep.directo.etapa === 'pasada' && !video.paused && !estado.esperando && estado.ultimoT != null) {
    const frontera = rep.frontera();
    if (estado.ultimoT <= frontera - MARGEN_ESPERA_S && t > frontera - MARGEN_ESPERA_S) {
      estado.esperando = true;
      estado.clavePildoras = '';
      video.pause();
    }
  }
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
  // [texto, color, texto corto para cuando el vídeo es estrecho, en directo]
  if (rep.enDirecto) {
    const frontera = rep.frontera();
    const que = estadoEnCurso(rep.directo);
    let vivo;
    if (que.clase !== 'directo') vivo = [que.texto, que.clase === 'pausa' ? COLORES.balon.i : '#f87171', que.corto];
    else if (rep.directo.etapa === 'pasada') vivo = [`En directo · analizado hasta ${minuto(frontera)}${que.pct != null ? ` (${que.pct} %)` : ''}`, COLORES.directo, 'Directo', true];
    else vivo = [`En directo · ${(ETAPAS[rep.directo.etapa] || 'analizando').toLowerCase()}`, COLORES.directo, 'Directo', true];
    if (t < rep.inicio - 0.1 || t > rep.fin + 0.1 || k < 0) {
      clave = `fuera${que.texto}`;
      lista = [vivo, ['Fuera del tramo analizado', '#64748b', 'Fuera del tramo']];
    } else if (t > frontera + rep.dt) {
      clave = `pendiente${que.texto}${estado.esperando}`;
      lista = [vivo, estado.esperando ? ['Esperando al analizador', COLORES.balon.i, 'Esperando'] : ['Aún sin analizar', '#64748b', 'Sin analizar']];
    } else {
      const b = rep.balon[k];
      clave = `${b}${rep.calibrado[k]}${que.texto}${estado.esperando}`;
      lista = [
        vivo,
        [b === '-' ? 'Sin balón' : b === 'n' ? 'Balón visto en un fotograma vecino' : 'Balón visto', COLORES.balon[b] || '#64748b', BALON_CORTO[b]],
        rep.calibrado[k] === '1' ? ['Campo calibrado', COLORES.calibrado, 'Calibrado'] : ['Sin calibrar', '#64748b', 'Sin calibrar'],
        estado.esperando ? ['Esperando al analizador', COLORES.balon.i, 'Esperando'] : null,
      ].filter(Boolean);
    }
  } else if (t < rep.inicio - 0.1 || t > rep.fin + 0.1 || k < 0) {
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
  $('pildoras').replaceChildren(...lista.map(([texto, color, corto, latiendo]) => el('span', { clase: `pildora${latiendo ? ' directo' : ''}` },
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
      caja.classList.remove('largo');
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
  $('btn-directo').addEventListener('click', irALoUltimo);
  $('btn-sonido').addEventListener('click', alternarSonido);
  video.addEventListener('volumechange', mostrarSonido);
  silenciar();
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

// Cada vídeo que se abre empieza sin sonido, para que no suene en segundo plano; se le da con el altavoz o la S.
function silenciar() {
  video.muted = true;
  mostrarSonido();
}

function alternarSonido() {
  video.muted = !video.muted;
  if (!video.muted && video.volume === 0) video.volume = 1;
}

function mostrarSonido() {
  const boton = $('btn-sonido');
  const con = !video.muted && video.volume > 0;
  boton.setAttribute('aria-pressed', String(con));
  boton.setAttribute('aria-label', con ? 'Quitar el sonido' : 'Activar el sonido');
  boton.title = con ? 'Con sonido: pulsa para quitarlo (S)' : 'Sin sonido: pulsa para oírlo (S)';
}

function alternarPlay() {
  if (!rep) return;
  estado.esperando = false;
  estado.clavePildoras = '';
  if (video.paused) video.play().catch(() => {});
  else video.pause();
}

function saltar(segundos) {
  if (!rep) return;
  irA(video.currentTime + estado.desfase + segundos);
}

function irA(t, quien) {
  if (!rep) return;
  estado.esperando = false;
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
  if (rep.enDirecto) {
    partes.push(p.clase === 3 ? 'Árbitro' : p.clase === 1 ? 'Portero (según el detector)' : 'Jugador');
    if (p.equipo === 0 || p.equipo === 1) partes.push(`${rep.equipos[p.equipo].nombre} (por la camiseta, hasta ahora)`);
    partes.push(`tramo #${p.tracklet} del seguidor · quién es se decide al terminar`);
    return partes.join(' · ');
  }
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
  if (!rep || e.target.closest('input, select, textarea, dialog')) return;
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  const tecla = e.key;
  if (tecla === ' ') { e.preventDefault(); alternarPlay(); return; }
  if (tecla === 'ArrowLeft') { e.preventDefault(); saltar(e.shiftKey ? -5 : -1); return; }
  if (tecla === 'ArrowRight') { e.preventDefault(); saltar(e.shiftKey ? 5 : 1); return; }
  if (tecla === ',') { instanteVecino(-1); return; }
  if (tecla === '.') { instanteVecino(1); return; }
  if (tecla === 'Escape') { elegir(null); return; }
  if (tecla.toLowerCase() === 'l' && rep.enDirecto) { irALoUltimo(); return; }
  if (tecla.toLowerCase() === 's') { alternarSonido(); return; }
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
