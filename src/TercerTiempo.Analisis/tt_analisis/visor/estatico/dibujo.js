// Lo que se dibuja encima del vídeo y el minimapa.

import { COLORES, aplicarH, colorPersona, proyectorCampo, textoSobre } from './util.js';

const LETRA_METODO = { aspecto: 'A', continuidad: 'C', descarte: 'D' };

// Las líneas del modelo del campo (cm): sus aristas, el círculo central y los puntos de penalti y de saque.
function lineasCampo(campo) {
  const lineas = campo.aristas.map(([a, b]) => [campo.vertices[a], campo.vertices[b]]);
  const circulo = [];
  const cx = campo.largo_cm / 2, cy = campo.ancho_cm / 2;
  for (let i = 0; i <= 64; i++) {
    const ang = (i / 64) * Math.PI * 2;
    circulo.push([cx + campo.circulo_cm * Math.cos(ang), cy + campo.circulo_cm * Math.sin(ang)]);
  }
  const puntos = [[cx, cy], [campo.penalti_cm, cy], [campo.largo_cm - campo.penalti_cm, cy]];
  return { lineas, circulo, puntos };
}

function muestrear(a, b, n) {
  const salida = [];
  for (let i = 0; i <= n; i++) salida.push([a[0] + (b[0] - a[0]) * (i / n), a[1] + (b[1] - a[1]) * (i / n)]);
  return salida;
}

export class Dibujo {
  constructor(rep) {
    this.rep = rep;
    this.geo = lineasCampo(rep.campo);
    this.cacheProyector = new Map();
  }

  proyector(h) {
    if (!h) return null;
    let p = this.cacheProyector.get(h);
    if (p === undefined) {
      p = proyectorCampo(h, this.rep.video.ancho, this.rep.video.alto);
      this.cacheProyector.set(h, p);
      if (this.cacheProyector.size > 400) this.cacheProyector.delete(this.cacheProyector.keys().next().value);
    }
    return p;
  }

  // `escena`: {escala, ox, oy} del vídeo en el lienzo. Devuelve las personas dibujadas (para saber cuál se pulsa).
  dibujar(ctx, escena, t, capas, estado) {
    const rep = this.rep;
    ctx.save();
    ctx.translate(escena.ox, escena.oy);
    ctx.scale(escena.escala, escena.escala);
    const px = 1 / escena.escala;
    const dentro = t >= rep.inicio - 0.05 && t <= rep.fin + 0.05;
    let personas = [];
    if (dentro) {
      if (capas.campo) this._campo(ctx, t, px);
      personas = rep.personasEn(t);
      if (capas.posesion) this._posesion(ctx, t, personas, px);
      if (capas.jugadores) this._personas(ctx, personas, capas, estado, px);
      if (capas.depuracion) this._descartados(ctx, t, px);
      if (capas.balon) this._balon(ctx, t, capas.estela, px);
    }
    ctx.restore();
    return personas;
  }

  _campo(ctx, t, px) {
    const { a, b, alfa, cerca } = this.rep.par(t);
    const pa = a && b ? this.proyector(a.h) : null;
    const pb = a && b ? this.proyector(b.h) : null;
    const uno = !(pa && pb) ? this.proyector((cerca || {}).h) : null;
    if (!(pa && pb) && !uno) return;
    const proyectar = (x, y) => {
      if (uno) return uno(x, y);
      const p = pa(x, y), q = pb(x, y);
      if (!p || !q) return null;
      return [p[0] + (q[0] - p[0]) * alfa, p[1] + (q[1] - p[1]) * alfa];
    };
    ctx.save();
    ctx.strokeStyle = 'rgba(255,255,255,0.55)';
    ctx.lineWidth = 2 * px;
    ctx.setLineDash([]);
    const trazar = (puntos) => {
      ctx.beginPath();
      let abierto = false;
      for (const [x, y] of puntos) {
        const p = proyectar(x, y);
        if (!p) { abierto = false; continue; }
        if (abierto) ctx.lineTo(p[0], p[1]);
        else { ctx.moveTo(p[0], p[1]); abierto = true; }
      }
      ctx.stroke();
    };
    for (const [p, q] of this.geo.lineas) trazar(muestrear(p, q, 24));
    trazar(this.geo.circulo);
    ctx.fillStyle = 'rgba(255,255,255,0.7)';
    for (const [x, y] of this.geo.puntos) {
      const p = proyectar(x, y);
      if (p) { ctx.beginPath(); ctx.arc(p[0], p[1], 3 * px, 0, Math.PI * 2); ctx.fill(); }
    }
    ctx.restore();
  }

  _posesion(ctx, t, personas, px) {
    const toque = this.rep.toqueEn(t);
    if (!toque) return;
    const p = personas.find((x) => x.id === toque.id);
    if (!p) return;
    const [x1, , x2, y2] = p.caja;
    const rx = Math.max(14 * px, (x2 - x1) * 0.75);
    ctx.save();
    ctx.strokeStyle = COLORES.equipo[toque.equipo] || COLORES.sinEquipo;
    ctx.lineWidth = 3 * px;
    ctx.shadowColor = '#000';
    ctx.shadowBlur = 4 * px;
    ctx.beginPath();
    ctx.ellipse((x1 + x2) / 2, y2, rx, rx * 0.32, 0, 0, Math.PI * 2);
    ctx.stroke();
    ctx.restore();
  }

  _personas(ctx, personas, capas, estado, px) {
    const rep = this.rep;
    const fuente = 12 * px;
    ctx.save();
    ctx.font = `600 ${fuente}px "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`;
    ctx.textBaseline = 'middle';
    for (const p of personas) {
      const color = colorPersona(p);
      const jugador = rep.jugadores.get(p.id);
      const elegido = estado.seleccion != null && estado.seleccion === p.id;
      const [x1, y1, x2, y2] = p.caja;
      ctx.lineWidth = (elegido ? 3.5 : 1.8) * px;
      ctx.strokeStyle = color;
      ctx.setLineDash(jugador || p.clase === 3 ? [] : [5 * px, 4 * px]);
      if (elegido) { ctx.shadowColor = color; ctx.shadowBlur = 10 * px; }
      ctx.strokeRect(x1, y1, x2 - x1, y2 - y1);
      ctx.shadowBlur = 0;
      ctx.setLineDash([]);
      if (!capas.etiquetas && !elegido) continue;
      const info = rep.tracklets.get(p.tracklet);
      let texto;
      if (p.clase === 3) texto = 'Árbitro';
      else if (rep.enDirecto) texto = p.clase === 1 ? `#${p.tracklet} · portero` : `#${p.tracklet}`;
      else if (jugador) {
        texto = String(jugador.dorsal);
        if (p.clase === 1) texto += ' · portero';
        const letra = info && LETRA_METODO[info.metodo];
        if (letra) texto += ` · ${letra}`;
        if (elegido && jugador.nombre) texto += ` · ${rep.nombreDe(p.id).split(' · ')[1] || ''}`;
      } else texto = p.clase === 1 ? '? · portero' : '?';
      if (capas.depuracion && !rep.enDirecto) {
        texto += ` #${p.tracklet}`;
        if (p.seguidor !== p.tracklet) texto += ` ↩${p.seguidor}`;
        if (info) texto += ` c${info.cadena}`;
      }
      const ancho = ctx.measureText(texto).width + 10 * px;
      const alto = 17 * px;
      const lx = (x1 + x2) / 2 - ancho / 2;
      const ly = y1 - alto - 3 * px;
      ctx.fillStyle = color;
      ctx.globalAlpha = jugador || p.clase === 3 ? 0.95 : 0.7;
      redondeado(ctx, lx, ly, ancho, alto, 4 * px);
      ctx.fill();
      ctx.globalAlpha = 1;
      ctx.fillStyle = textoSobre(color);
      ctx.fillText(texto, lx + 5 * px, ly + alto / 2 + 0.5 * px);
    }
    ctx.restore();
  }

  _balon(ctx, t, conEstela, px) {
    const rep = this.rep;
    ctx.save();
    if (conEstela) {
      const puntos = rep.estela(t, 1.2);
      for (let i = 1; i < puntos.length; i++) {
        const a = puntos[i - 1], b = puntos[i];
        if (b.t - a.t > 0.5) continue;
        const edad = (t - b.t) / 1.2;
        ctx.globalAlpha = Math.max(0.1, 0.85 * (1 - edad));
        ctx.strokeStyle = COLORES.balon[b.o === 'i' || a.o === 'i' ? 'i' : b.o] || '#fff';
        ctx.lineWidth = 3 * px;
        ctx.setLineDash(b.o === 'i' || a.o === 'i' ? [6 * px, 5 * px] : []);
        ctx.beginPath();
        ctx.moveTo(a.x, a.y);
        ctx.lineTo(b.x, b.y);
        ctx.stroke();
      }
      ctx.globalAlpha = 1;
      ctx.setLineDash([]);
    }
    const balon = rep.balonEn(t);
    if (balon) {
      const color = COLORES.balon[balon.o] || '#fff';
      ctx.shadowColor = '#000';
      ctx.shadowBlur = 6 * px;
      ctx.lineWidth = 3 * px;
      ctx.strokeStyle = color;
      ctx.setLineDash(balon.o === 'i' ? [4 * px, 3 * px] : []);
      ctx.beginPath();
      ctx.arc(balon.x, balon.y, 9 * px, 0, Math.PI * 2);
      ctx.stroke();
      if (balon.o !== 'i') {
        ctx.fillStyle = '#ffffffd0';
        ctx.beginPath();
        ctx.arc(balon.x, balon.y, 3.2 * px, 0, Math.PI * 2);
        ctx.fill();
      }
    }
    ctx.restore();
  }

  _descartados(ctx, t, px) {
    ctx.save();
    ctx.strokeStyle = COLORES.balon.x;
    ctx.lineWidth = 2.5 * px;
    for (const d of this.rep.descartadosCerca(t)) {
      const r = 8 * px;
      ctx.beginPath();
      ctx.moveTo(d.x - r, d.y - r); ctx.lineTo(d.x + r, d.y + r);
      ctx.moveTo(d.x + r, d.y - r); ctx.lineTo(d.x - r, d.y + r);
      ctx.stroke();
    }
    ctx.restore();
  }

  // El campo visto desde arriba: dónde ve el analizador a cada uno (los pies llevados al campo con la calibración de su
  // fotograma), el balón y lo que abarca la cámara.
  minimapa(ctx, ancho, alto, t, personas, estado) {
    const rep = this.rep;
    const campo = rep.campo;
    const margen = 8;
    const escala = Math.min((ancho - 2 * margen) / campo.largo_cm, (alto - 2 * margen) / campo.ancho_cm);
    const ox = (ancho - campo.largo_cm * escala) / 2;
    const oy = (alto - campo.ancho_cm * escala) / 2;
    const a = (x, y) => [ox + x * escala, oy + y * escala];
    ctx.save();
    ctx.clearRect(0, 0, ancho, alto);
    ctx.fillStyle = '#0d2b1c';
    ctx.globalAlpha = 0.92;
    ctx.fillRect(0, 0, ancho, alto);
    ctx.globalAlpha = 1;
    ctx.strokeStyle = 'rgba(255,255,255,0.45)';
    ctx.lineWidth = 1;
    for (const [p, q] of this.geo.lineas) {
      const [x1, y1] = a(p[0], p[1]); const [x2, y2] = a(q[0], q[1]);
      ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
    }
    ctx.beginPath();
    this.geo.circulo.forEach(([x, y], i) => { const [u, v] = a(x, y); if (i) ctx.lineTo(u, v); else ctx.moveTo(u, v); });
    ctx.stroke();

    const { cerca } = rep.par(t);
    const h = cerca ? cerca.h : null;
    if (!h) {
      const lineas = t < rep.inicio || t > rep.fin ? ['Fuera del tramo analizado'] : ['Sin calibrar', 'no se sabe dónde está cada uno'];
      ctx.fillStyle = 'rgba(255,255,255,0.8)';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      lineas.forEach((linea, i) => {
        let tam = i ? 11 : 13;
        do ctx.font = `${i ? 400 : 600} ${tam}px "Segoe UI", system-ui, sans-serif`;
        while (ctx.measureText(linea).width > ancho - 2 * margen && --tam > 7);
        ctx.fillText(linea, ancho / 2, alto / 2 + (i - (lineas.length - 1) / 2) * 17);
      });
      ctx.restore();
      return;
    }
    const vista = this._vista(h);
    if (vista) {
      ctx.save();
      ctx.beginPath();
      ctx.rect(ox - 12, oy - 12, campo.largo_cm * escala + 24, campo.ancho_cm * escala + 24);
      ctx.clip();
      ctx.fillStyle = 'rgba(255,255,255,0.10)';
      ctx.strokeStyle = 'rgba(255,255,255,0.35)';
      ctx.beginPath();
      vista.forEach(([x, y], i) => { const [u, v] = a(x, y); if (i) ctx.lineTo(u, v); else ctx.moveTo(u, v); });
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
      ctx.restore();
    }
    ctx.font = '700 9px "Segoe UI", system-ui, sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    for (const p of personas) {
      if (!p.h) continue;
      const pie = aplicarH(p.h, (p.caja[0] + p.caja[2]) / 2, p.caja[3]);
      if (!pie) continue;
      const [u, v] = a(pie[0], pie[1]);
      if (u < -10 || v < -10 || u > ancho + 10 || v > alto + 10) continue;
      const jugador = rep.jugadores.get(p.id);
      const elegido = estado.seleccion != null && estado.seleccion === p.id;
      const r = elegido ? 7.5 : jugador ? 6 : 4;
      ctx.fillStyle = colorPersona(p);
      ctx.globalAlpha = jugador || p.clase === 3 ? 1 : 0.7;
      ctx.beginPath(); ctx.arc(u, v, r, 0, Math.PI * 2); ctx.fill();
      ctx.globalAlpha = 1;
      if (elegido) { ctx.strokeStyle = '#fff'; ctx.lineWidth = 2; ctx.stroke(); }
      if (jugador) { ctx.fillStyle = textoSobre(colorPersona(p)); ctx.fillText(String(jugador.dorsal), u, v + 0.5); }
    }
    const balon = rep.balonCampoEn(t);
    if (balon) {
      const [u, v] = a(balon[0], balon[1]);
      ctx.fillStyle = '#fff';
      ctx.strokeStyle = '#000';
      ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.arc(u, v, 4, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
    }
    ctx.restore();
  }

  // Lo que abarca la cámara en el campo: las esquinas de abajo y, arriba, la primera altura de la imagen que aún cae
  // en el campo (la de arriba del todo puede quedar por encima del horizonte).
  _vista(h) {
    const { ancho, alto } = this.rep.video;
    const lejos = 4 * Math.max(this.rep.campo.largo_cm, this.rep.campo.ancho_cm);
    const ref = aplicarH(h, ancho / 2, alto);
    if (!ref) return null;
    const signo = Math.sign(ref[2]);
    const valido = (p) => p && Math.sign(p[2]) === signo && Math.abs(p[0]) < lejos && Math.abs(p[1]) < lejos;
    const abajoIzq = aplicarH(h, 0, alto), abajoDer = aplicarH(h, ancho, alto);
    if (!valido(abajoIzq) || !valido(abajoDer)) return null;
    for (const f of [0, 0.08, 0.16, 0.24, 0.32, 0.4, 0.5]) {
      const arribaIzq = aplicarH(h, 0, alto * f), arribaDer = aplicarH(h, ancho, alto * f);
      if (valido(arribaIzq) && valido(arribaDer)) return [abajoIzq, abajoDer, arribaDer, arribaIzq];
    }
    return null;
  }
}

function redondeado(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}
