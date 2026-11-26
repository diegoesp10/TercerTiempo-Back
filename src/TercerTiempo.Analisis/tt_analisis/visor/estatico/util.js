// Utilidades del visor: formato de tiempos, colores, homografías y DOM.

export const COLORES = {
  equipo: ['#38bdf8', '#fb923c'],
  arbitro: '#facc15',
  sinEquipo: '#cbd5e1',
  balon: { v: '#4ade80', n: '#2dd4bf', i: '#fbbf24', x: '#f87171' },
  calibrado: '#818cf8',
  enJuego: '#cbd5e1',
  directo: '#f43f5e',
};

// Las etapas de un análisis (`avance.ETAPAS`), como se enseñan mientras se hace.
export const ETAPAS = {
  modelos: 'Cargando los modelos',
  equipos: 'Separando los equipos por la camiseta',
  pasada: 'Siguiendo a jugadores y balón',
  jugadas: 'Balón, porteros, quién es quién y jugadas',
  revision: 'Revisando con el modelo de lenguaje',
  salidas: 'Escribiendo los resultados',
};

// Cómo va un análisis en marcha (lo de directo.json): texto, texto corto, clase ('directo', 'pausa' o 'mal'), si se ha
// parado (o ya no llega nada de él) y, en la pasada, el porcentaje.
export function estadoEnCurso(e) {
  if (!e) return { texto: 'Analizando', corto: 'Analizando', clase: 'directo', parado: false, pct: null };
  if (e.sin_directo) return { texto: 'Ya no llega nada en directo', corto: 'Sin directo', clase: 'pausa', parado: true, pct: null };
  if (e.error) return { texto: `Falló: ${e.error}`, corto: 'Falló', clase: 'mal', parado: true, pct: null };
  if (e.vivo === false) return { texto: `Se ha parado${e.actualizado ? ` (sin noticias desde las ${hora(e.actualizado)})` : ''}`, corto: 'Parado', clase: 'mal', parado: true, pct: null };
  const pct = e.etapa === 'pasada' && e.fotogramas_total && e.fotogramas != null ? Math.min(100, Math.floor((100 * e.fotogramas) / e.fotogramas_total)) : null;
  if (e.pausado) return { texto: 'En pausa', corto: 'En pausa', clase: 'pausa', parado: false, pct };
  if (pct != null) return { texto: `Analizando · ${pct} %`, corto: `${pct} %`, clase: 'directo', parado: false, pct };
  return { texto: ETAPAS[e.etapa] || 'Analizando', corto: 'Analizando', clase: 'directo', parado: false, pct };
}

// La hora de un instante (segundos desde 1970), «19:42».
export function hora(segundos) {
  return new Date(segundos * 1000).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
}

export const ORIGEN_BALON = {
  v: 'visto en su fotograma',
  n: 'visto en un fotograma vecino',
  i: 'interpolado',
  x: 'visto y descartado',
  '-': 'sin balón',
};

export const METODO = {
  dorsal: 'por el dorsal',
  aspecto: 'por el aspecto',
  continuidad: 'por continuidad',
  descarte: 'por descarte',
};

export const TIPOS_EVENTO = {
  gol: 'Gol',
  tiro: 'Tiro',
  parada: 'Parada',
  falta: 'Falta',
  fuera_de_juego: 'Fuera de juego',
  corner: 'Córner',
  saque_banda: 'Saque de banda',
  saque_puerta: 'Saque de puerta',
  saque_centro: 'Saque de centro',
  fuera_fondo: 'Fuera por la línea de fondo',
  fuera_banda: 'Fuera por la banda',
  paron: 'Parón',
};

export const TIPOS_ACCION = {
  pases: 'Pase',
  pases_fallados: 'Pase fallado',
  perdidas: 'Pérdida',
  recuperaciones: 'Recuperación',
  paradas: 'Parada',
  saques: 'Saque del portero',
};

export const ACCIONES_PLURAL = {
  pases: 'Pases',
  pases_fallados: 'Pases fallados',
  perdidas: 'Pérdidas',
  recuperaciones: 'Recuperaciones',
  paradas: 'Paradas',
  saques: 'Saques del portero',
};

const dos = (n) => String(n).padStart(2, '0');

export function minuto(t) {
  if (t == null || !Number.isFinite(t)) return '–';
  const s = Math.max(0, Math.floor(t));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h ? `${h}:${dos(m)}:${dos(s % 60)}` : `${dos(m)}:${dos(s % 60)}`;
}

export function minutoPreciso(t) {
  if (t == null || !Number.isFinite(t)) return '–';
  // En décimas enteras: 143,2 s se guarda como 143,1999… y no debe salir como 02:23,1.
  const decimas = Math.floor(Math.max(0, t) * 10 + 1e-6);
  return `${minuto(Math.floor(decimas / 10))},${decimas % 10}`;
}

export function duracion(s) {
  if (s == null || !Number.isFinite(s)) return '–';
  const m = Math.floor(s / 60);
  return m ? `${m}:${dos(Math.floor(s % 60))}` : `${Math.round(s)} s`;
}

export function pct(a, b, decimales = 0) {
  if (!b) return '–';
  return `${(100 * a / b).toLocaleString('es-ES', { maximumFractionDigits: decimales, minimumFractionDigits: decimales })} %`;
}

export function numero(v, decimales = 0) {
  if (v == null || !Number.isFinite(Number(v))) return '–';
  return Number(v).toLocaleString('es-ES', { maximumFractionDigits: decimales, minimumFractionDigits: decimales });
}

export function colorPersona(p) {
  if (p.clase === 3) return COLORES.arbitro;
  if (p.equipo === 0 || p.equipo === 1) return COLORES.equipo[p.equipo];
  return COLORES.sinEquipo;
}

export function textoSobre(hex) {
  const v = parseInt(hex.slice(1), 16);
  const r = (v >> 16) & 255, g = (v >> 8) & 255, b = v & 255;
  return (0.299 * r + 0.587 * g + 0.114 * b) > 150 ? '#0a0e13' : '#ffffff';
}

// Último índice con lista[i] <= t (-1 si no hay ninguno).
export function buscarIndice(lista, t) {
  let a = 0, b = lista.length - 1, r = -1;
  while (a <= b) {
    const m = (a + b) >> 1;
    if (lista[m] <= t) { r = m; a = m + 1; } else b = m - 1;
  }
  return r;
}

// Homografía 3 × 3 por filas: [x, y, w] del punto, o null si w es casi cero.
export function aplicarH(h, x, y) {
  const w = h[6] * x + h[7] * y + h[8];
  if (Math.abs(w) < 1e-12) return null;
  return [(h[0] * x + h[1] * y + h[2]) / w, (h[3] * x + h[4] * y + h[5]) / w, w];
}

export function invertir(h) {
  const [a, b, c, d, e, f, g, i, k] = h;
  const A = e * k - f * i, B = -(d * k - f * g), C = d * i - e * g;
  const det = a * A + b * B + c * C;
  if (Math.abs(det) < 1e-18) return null;
  return [
    A / det, -(b * k - c * i) / det, (b * f - c * e) / det,
    B / det, (a * k - c * g) / det, -(a * f - c * d) / det,
    C / det, -(a * i - b * g) / det, (a * e - b * d) / det,
  ];
}

// Del campo (cm) a la imagen con la homografía imagen → campo `h` de un fotograma: los puntos del campo que quedan
// detrás de la cámara (al otro lado del horizonte) no tienen sitio en la imagen.
export function proyectorCampo(h, ancho, alto) {
  const inversa = invertir(h);
  if (!inversa) return null;
  const centro = aplicarH(h, ancho / 2, alto / 2);
  if (!centro) return null;
  const ref = aplicarH(inversa, centro[0], centro[1]);
  if (!ref) return null;
  const signo = Math.sign(ref[2]);
  const limite = 6 * Math.max(ancho, alto);
  return (x, y) => {
    const p = aplicarH(inversa, x, y);
    if (!p || Math.sign(p[2]) !== signo) return null;
    if (Math.abs(p[0]) > limite || Math.abs(p[1]) > limite) return null;
    return p;
  };
}

export function el(etiqueta, atributos = {}, ...hijos) {
  const nodo = document.createElement(etiqueta);
  for (const [k, v] of Object.entries(atributos || {})) {
    if (v == null || v === false) continue;
    if (k === 'clase') nodo.className = v;
    else if (k === 'texto') nodo.textContent = v;
    else if (k.startsWith('on')) nodo.addEventListener(k.slice(2), v);
    else if (k === 'estilo') Object.assign(nodo.style, v);
    else nodo.setAttribute(k, v === true ? '' : v);
  }
  for (const h of hijos.flat()) {
    if (h == null || h === false) continue;
    nodo.append(h instanceof Node ? h : document.createTextNode(String(h)));
  }
  return nodo;
}

export function guardar(clave, valor) {
  try { localStorage.setItem(clave, JSON.stringify(valor)); } catch { /* sin almacenamiento: no pasa nada */ }
}

export function recordar(clave, porDefecto) {
  try {
    const v = localStorage.getItem(clave);
    return v == null ? porDefecto : JSON.parse(v);
  } catch {
    return porDefecto;
  }
}
