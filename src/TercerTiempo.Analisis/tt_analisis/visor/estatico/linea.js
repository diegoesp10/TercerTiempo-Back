// Las dos líneas de tiempo: la de todo el tramo y la de detalle (30 s antes y después de donde está el vídeo). En cada
// una, por instante analizado: de dónde sale el balón, si está en juego, a quién se le cuenta la posesión y si el campo
// está calibrado; en la de detalle, además, los toques (quién tiene el balón) y cada acción que cuenta. En directo, lo
// que aún no se ha analizado va sombreado y lo que solo se sabe al terminar (en juego, posesión) queda vacío.

import { COLORES, TIPOS_ACCION, TIPOS_EVENTO, buscarIndice, minuto } from './util.js';

const FILAS = ['Balón', 'En juego', 'Posesión', 'Campo'];
const IZQUIERDA = 74;
const VENTANA_DETALLE_S = 30;
const LETRA_ACCION = { pases: 'P', pases_fallados: 'PF', perdidas: 'Pé', recuperaciones: 'R', paradas: 'Pa', saques: 'S' };

function colorFila(rep, fila, k) {
  if (fila === 0) return COLORES.balon[rep.balon[k]] || null;
  if (fila === 1) return rep.enJuego[k] === '1' ? '#94a3b8' : null;
  if (fila === 2) {
    const q = rep.posesion[k];
    return q === '0' || q === '1' ? COLORES.equipo[Number(q)] : null;
  }
  return rep.calibrado[k] === '1' ? COLORES.calibrado : null;
}

export class Lineas {
  constructor(lienzoDetalle, lienzoTotal, rep, alBuscar) {
    this.detalle = lienzoDetalle;
    this.total = lienzoTotal;
    this.rep = rep;
    this.alBuscar = alBuscar;
    this.fondo = document.createElement('canvas');
    this.arrastrando = false;
    const buscar = (e, lienzo, deX) => {
      const r = lienzo.getBoundingClientRect();
      const x = e.clientX - r.left;
      if (x < IZQUIERDA - 4) return;
      this.alBuscar(deX(x, r.width));
    };
    const totalX = (x, w) => this.rep.inicio + (Math.min(Math.max(x, IZQUIERDA), w - 8) - IZQUIERDA) / Math.max(1, w - IZQUIERDA - 8) * (this.rep.fin - this.rep.inicio);
    this.total.addEventListener('pointerdown', (e) => { this.arrastrando = true; this.total.setPointerCapture(e.pointerId); buscar(e, this.total, totalX); });
    this.total.addEventListener('pointermove', (e) => {
      if (this.arrastrando) buscar(e, this.total, totalX);
      this._ayuda(e, this.total, totalX);
    });
    this.total.addEventListener('pointerup', () => { this.arrastrando = false; });
    this.detalle.addEventListener('pointerdown', (e) => buscar(e, this.detalle, (x, w) => this.t - VENTANA_DETALLE_S + (x - IZQUIERDA) / Math.max(1, w - IZQUIERDA - 8) * 2 * VENTANA_DETALLE_S));
    this.detalle.addEventListener('pointermove', (e) => this._ayuda(e, this.detalle, (x, w) => this.t - VENTANA_DETALLE_S + (x - IZQUIERDA) / Math.max(1, w - IZQUIERDA - 8) * 2 * VENTANA_DETALLE_S));
    this.t = rep.inicio;
    this.redimensionar();
  }

  _ayuda(e, lienzo, deX) {
    const r = lienzo.getBoundingClientRect();
    const x = e.clientX - r.left;
    if (x < IZQUIERDA - 4) { lienzo.title = ''; return; }
    const t = deX(x, r.width);
    const k = this.rep.instante(t);
    if (k < 0) return;
    const rep = this.rep;
    if (rep.enDirecto) {
      if (t > rep.frontera() + rep.dt) { lienzo.title = `${minuto(t)} · aún sin analizar`; return; }
      const balon = { v: 'visto', n: 'visto en un vecino', '-': 'sin balón' }[rep.balon[k]] || 'sin balón';
      lienzo.title = `${minuto(t)} · balón ${balon} · ${rep.calibrado[k] === '1' ? 'calibrado' : 'sin calibrar'} · en juego y posesión, al terminar`;
      return;
    }
    const balon = { v: 'visto', n: 'visto en un vecino', i: 'interpolado', x: 'descartado', '-': 'sin balón' }[rep.balon[k]];
    const q = rep.posesion[k];
    const pos = q === '0' || q === '1' ? rep.equipos[Number(q)].nombre : 'a nadie';
    lienzo.title = `${minuto(t)} · balón ${balon} · ${rep.enJuego[k] === '1' ? 'en juego' : 'parado'} · posesión: ${pos} · ${rep.calibrado[k] === '1' ? 'calibrado' : 'sin calibrar'}`;
  }

  redimensionar() {
    const dpr = window.devicePixelRatio || 1;
    for (const lienzo of [this.detalle, this.total]) {
      const r = lienzo.getBoundingClientRect();
      lienzo.width = Math.max(1, Math.round(r.width * dpr));
      lienzo.height = Math.max(1, Math.round(r.height * dpr));
    }
    this.fondo.width = this.total.width;
    this.fondo.height = this.total.height;
    this._pintarFondo();
  }

  // En directo, con instantes nuevos.
  actualizar() {
    this._pintarFondo();
  }

  // Lo que aún no ha analizado (en directo): sombreado desde `x0`, con su rótulo si cabe.
  _pendiente(ctx, x0, x1, y0, y1) {
    if (x1 - x0 < 1) return;
    ctx.save();
    ctx.beginPath();
    ctx.rect(x0, y0, x1 - x0, y1 - y0);
    ctx.clip();
    ctx.fillStyle = '#0a0e13b8';
    ctx.fillRect(x0, y0, x1 - x0, y1 - y0);
    ctx.strokeStyle = '#ffffff12';
    ctx.lineWidth = 1;
    ctx.beginPath();
    const alto = y1 - y0;
    for (let x = x0 - alto; x < x1; x += 7) {
      ctx.moveTo(x, y1);
      ctx.lineTo(x + alto, y0);
    }
    ctx.stroke();
    ctx.restore();
    ctx.save();
    ctx.fillStyle = COLORES.directo;
    ctx.fillRect(x0 - 1, y0, 2, y1 - y0);
    ctx.font = '600 10px "Segoe UI", system-ui, sans-serif';
    ctx.textBaseline = 'middle';
    const texto = 'Sin analizar todavía';
    if (x1 - x0 > ctx.measureText(texto).width + 16) {
      ctx.fillStyle = '#93a1b3';
      ctx.fillText(texto, x0 + 8, (y0 + y1) / 2);
    }
    ctx.restore();
  }

  _pintarFondo() {
    const rep = this.rep;
    const dpr = window.devicePixelRatio || 1;
    const ctx = this.fondo.getContext('2d');
    const w = this.fondo.width / dpr, h = this.fondo.height / dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    const ancho = w - IZQUIERDA - 8;
    const x = (t) => IZQUIERDA + (t - rep.inicio) / Math.max(1e-6, rep.fin - rep.inicio) * ancho;
    const altoFila = 9, gap = 3, arriba = 4;
    this._rotulos(ctx, arriba, altoFila, gap);
    for (let fila = 0; fila < FILAS.length; fila++) {
      const y = arriba + fila * (altoFila + gap);
      ctx.fillStyle = '#ffffff0a';
      ctx.fillRect(IZQUIERDA, y, ancho, altoFila);
      for (let k = 0; k < rep.t.length; k++) {
        const color = colorFila(rep, fila, k);
        if (!color) continue;
        const x0 = x(rep.t[k]);
        ctx.fillStyle = color;
        ctx.fillRect(x0, y, Math.max(1, x(rep.t[k] + rep.dt) - x0), altoFila);
      }
      if (rep.enDirecto && (fila === 1 || fila === 2) && rep.t.length) {
        ctx.font = '9px "Segoe UI", system-ui, sans-serif';
        ctx.fillStyle = '#64748b';
        ctx.textBaseline = 'middle';
        ctx.fillText('al terminar', IZQUIERDA + 4, y + altoFila / 2 + 0.5);
      }
    }
    if (rep.enDirecto) this._pendiente(ctx, Math.max(IZQUIERDA, x(rep.frontera() + rep.dt)), IZQUIERDA + ancho, 0, arriba + FILAS.length * (altoFila + gap));
    const yEv = arriba + FILAS.length * (altoFila + gap) + 2;
    ctx.font = '600 10px "Segoe UI", system-ui, sans-serif';
    ctx.fillStyle = '#93a1b3';
    ctx.textBaseline = 'middle';
    ctx.fillText('Jugadas', 6, yEv + 5);
    for (const e of rep.eventos) {
      if (e.tipo === 'paron') continue;
      ctx.fillStyle = e.equipo === 0 || e.equipo === 1 ? COLORES.equipo[e.equipo] : '#e2e8f0';
      const xe = x(e.t);
      if (e.tipo === 'gol') { ctx.beginPath(); ctx.arc(xe, yEv + 5, 4, 0, Math.PI * 2); ctx.fill(); } else ctx.fillRect(xe - 1, yEv, 2, 10);
    }
    if (rep.final.descanso_s != null && rep.final.descanso_s > rep.inicio && rep.final.descanso_s < rep.fin) {
      ctx.fillStyle = '#e2e8f0';
      ctx.fillRect(x(rep.final.descanso_s) - 0.5, 0, 1, h);
    }
    ctx.fillStyle = '#64748b';
    ctx.font = '10px "Segoe UI", system-ui, sans-serif';
    ctx.textBaseline = 'alphabetic';
    // Marcas cada minuto o más espaciadas: como mucho 12 y que los rótulos no se pisen.
    const pasos = [60, 120, 300, 600, 900, 1800, 2700];
    const pxPorS = ancho / Math.max(1e-6, rep.fin - rep.inicio);
    const paso = pasos.find((p) => (rep.fin - rep.inicio) / p <= 12 && p * pxPorS >= 46) || pasos[pasos.length - 1];
    const marcas = [];
    for (let t = Math.ceil(rep.inicio / paso) * paso; t <= rep.fin; t += paso) {
      ctx.fillRect(x(t), h - 12, 1, 4);
      marcas.push(t);
    }
    rotularTiempos(ctx, marcas, x, w - 2, h - 2);
  }

  _rotulos(ctx, arriba, altoFila, gap) {
    ctx.font = '600 10px "Segoe UI", system-ui, sans-serif';
    ctx.fillStyle = '#93a1b3';
    ctx.textBaseline = 'middle';
    FILAS.forEach((nombre, fila) => ctx.fillText(nombre, 6, arriba + fila * (altoFila + gap) + altoFila / 2 + 0.5));
  }

  dibujar(t) {
    this.t = t;
    const rep = this.rep;
    const dpr = window.devicePixelRatio || 1;
    // Todo el tramo: el fondo y dónde está el vídeo.
    let ctx = this.total.getContext('2d');
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, this.total.width, this.total.height);
    ctx.drawImage(this.fondo, 0, 0);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    let w = this.total.width / dpr;
    let h = this.total.height / dpr;
    const xt = IZQUIERDA + (t - rep.inicio) / Math.max(1e-6, rep.fin - rep.inicio) * (w - IZQUIERDA - 8);
    if (xt >= IZQUIERDA && xt <= w) {
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(xt - 1, 0, 2, h);
      const xa = IZQUIERDA + (t - VENTANA_DETALLE_S - rep.inicio) / Math.max(1e-6, rep.fin - rep.inicio) * (w - IZQUIERDA - 8);
      ctx.fillStyle = '#ffffff14';
      ctx.fillRect(xa, 0, 2 * (xt - xa), h);
    }

    // El detalle.
    ctx = this.detalle.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    w = this.detalle.width / dpr;
    h = this.detalle.height / dpr;
    ctx.clearRect(0, 0, w, h);
    const desde = t - VENTANA_DETALLE_S, hasta = t + VENTANA_DETALLE_S;
    const ancho = w - IZQUIERDA - 8;
    const x = (s) => IZQUIERDA + (s - desde) / (hasta - desde) * ancho;
    const altoFila = 9, gap = 3, arriba = 4;
    this._rotulos(ctx, arriba, altoFila, gap);
    const k0 = Math.max(0, rep.instante(desde));
    const k1 = Math.min(rep.t.length - 1, rep.instante(hasta) + 1);
    for (let fila = 0; fila < FILAS.length; fila++) {
      const y = arriba + fila * (altoFila + gap);
      ctx.fillStyle = '#ffffff0a';
      ctx.fillRect(IZQUIERDA, y, ancho, altoFila);
      for (let k = k0; k <= k1; k++) {
        const color = colorFila(rep, fila, k);
        if (!color) continue;
        const x0 = Math.max(IZQUIERDA, x(rep.t[k]));
        const x1 = Math.min(IZQUIERDA + ancho, x(rep.t[k] + rep.dt));
        if (x1 <= x0) continue;
        ctx.fillStyle = color;
        ctx.fillRect(x0, y, Math.max(1, x1 - x0 - 0.5), altoFila);
      }
    }
    if (rep.enDirecto) {
      const xf = x(rep.frontera() + rep.dt);
      if (xf < IZQUIERDA + ancho) this._pendiente(ctx, Math.max(IZQUIERDA, xf), IZQUIERDA + ancho, 0, h - 12);
    }
    // Los toques: quién tiene el balón (con su dorsal si cabe).
    const yToques = arriba + FILAS.length * (altoFila + gap);
    ctx.font = '600 10px "Segoe UI", system-ui, sans-serif';
    ctx.textBaseline = 'middle';
    ctx.fillStyle = '#93a1b3';
    ctx.fillText('Toques', 6, yToques + 6);
    const i0 = Math.max(0, buscarIndice(rep.toquesT0, desde) - 1);
    for (let i = i0; i < rep.toques.length && rep.toques[i].t0 <= hasta; i++) {
      const toque = rep.toques[i];
      if (toque.t1 < desde) continue;
      const x0 = Math.max(IZQUIERDA, x(toque.t0));
      const x1 = Math.min(IZQUIERDA + ancho, x(Math.max(toque.t1, toque.t0 + 0.2)));
      ctx.fillStyle = COLORES.equipo[toque.equipo] || '#cbd5e1';
      ctx.globalAlpha = toque.parado ? 0.45 : 0.9;
      ctx.fillRect(x0, yToques, Math.max(2, x1 - x0), 12);
      ctx.globalAlpha = 1;
      const j = rep.jugadores.get(toque.id);
      if (j && x1 - x0 > 14) {
        ctx.fillStyle = '#0a0e13';
        ctx.fillText(String(j.dorsal), x0 + 3, yToques + 6.5);
      }
    }
    // Cada acción que cuenta y cada jugada.
    const yAcc = yToques + 16;
    ctx.fillStyle = '#93a1b3';
    ctx.fillText('Cuenta', 6, yAcc + 8);
    const etiquetas = [];
    const m0 = Math.max(0, buscarIndice(rep.momentosT, desde));
    for (let i = m0; i < rep.momentos.length && rep.momentos[i].t <= hasta; i++) {
      const m = rep.momentos[i];
      if (m.t < desde) continue;
      const j = rep.jugadores.get(m.id);
      etiquetas.push({ t: m.t, texto: `${LETRA_ACCION[m.tipo] || m.tipo}${j ? ' ' + j.dorsal : ''}`, color: COLORES.equipo[m.equipo] || '#e2e8f0', titulo: TIPOS_ACCION[m.tipo] });
    }
    const e0 = Math.max(0, buscarIndice(rep.eventosT, desde));
    for (let i = e0; i < rep.eventos.length && rep.eventos[i].t <= hasta; i++) {
      const e = rep.eventos[i];
      if (e.t < desde || e.tipo === 'paron') continue;
      etiquetas.push({ t: e.t, texto: (TIPOS_EVENTO[e.tipo] || e.tipo).split(' ')[0], color: e.equipo === 0 || e.equipo === 1 ? COLORES.equipo[e.equipo] : '#e2e8f0', jugada: true });
    }
    etiquetas.sort((a, b) => a.t - b.t);
    let ultimoFin = [-Infinity, -Infinity];
    for (const et of etiquetas) {
      const xe = x(et.t);
      if (xe < IZQUIERDA || xe > IZQUIERDA + ancho) continue;
      const anchoTexto = ctx.measureText(et.texto).width + 6;
      // Pegado a la derecha, el rótulo se corre a la izquierda de su marca para que se lea entero.
      const xl = Math.min(xe, IZQUIERDA + ancho - anchoTexto);
      const carril = xl > ultimoFin[0] ? 0 : (xl > ultimoFin[1] ? 1 : -1);
      ctx.fillStyle = et.color;
      ctx.fillRect(xe - 0.5, yAcc, 1, 28);
      if (carril < 0) continue;
      const yy = yAcc + 2 + carril * 13;
      ctx.globalAlpha = et.jugada ? 1 : 0.9;
      ctx.fillRect(xl, yy, anchoTexto, 12);
      ctx.globalAlpha = 1;
      ctx.fillStyle = '#0a0e13';
      ctx.fillText(et.texto, xl + 3, yy + 6.5);
      ultimoFin[carril] = xl + anchoTexto + 2;
    }
    // Dónde está el vídeo.
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(x(t) - 1, 0, 2, h);
    ctx.fillStyle = '#64748b';
    ctx.font = '10px "Segoe UI", system-ui, sans-serif';
    ctx.textBaseline = 'alphabetic';
    const marcas = [];
    for (let s = Math.ceil(desde / 10) * 10; s <= hasta; s += 10) {
      if (x(s) < IZQUIERDA) continue;
      ctx.fillRect(x(s), h - 10, 1, 3);
      marcas.push(s);
    }
    rotularTiempos(ctx, marcas, x, w - 2, h - 1);
  }
}

// Los rótulos de tiempo de una regla, sin pisarse ni salirse por la derecha (el último se pone a la izquierda de su marca).
function rotularTiempos(ctx, tiempos, x, limite, y) {
  let fin = -Infinity;
  for (const s of tiempos) {
    const texto = minuto(s);
    const ancho = ctx.measureText(texto).width;
    let xl = x(s) + 2;
    if (xl + ancho > limite) xl = x(s) - 2 - ancho;
    if (xl <= fin + 4) continue;
    ctx.fillText(texto, xl, y);
    fin = xl + ancho;
  }
}
