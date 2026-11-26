// Etiquetas humanas para evaluar una pasada sin modificar su propuesta.
import { pedirJSON } from './datos.js';
import { duracion, el, minutoPreciso, numero } from './util.js';

const TIPOS = { tiro: 'Tiros', saque_puerta: 'Saques de puerta', parada: 'Paradas' };
const ESTADOS = { real: 'Real', falsa: 'Falsa', dudosa: 'No se ve', pendiente: 'Pendiente' };
const boton = (texto, pulsar, clase = '') => el('button', { type: 'button', clase: `boton ${clase}`, texto, onclick: pulsar });

export class RevisionEventos {
  constructor(rep, acciones, refrescar) {
    this.url = `/api/a/${rep.id}/revision-eventos`;
    this.rep = rep;
    this.acciones = acciones;
    this.refrescar = refrescar;
    this.tipo = 'tiro';
    this.pendientes = true;
    this.elegido = null;
    this.inicios = {};
    this.nota = null;
    this.datos = null;
    this.ocupado = false;
    this.error = '';
    this.aviso = '';
  }

  async cargar() {
    if (this.ocupado) return;
    this.error = '';
    this.ocupado = true;
    try {
      const { fuente, revision, medicion } = await pedirJSON(this.url);
      this.fuente = fuente;
      this.datos = revision;
      this.medicion = medicion;
    } catch (e) {
      this.error = e.message;
    } finally {
      this.ocupado = false;
      this.refrescar();
    }
  }

  async editar(orden, despues = () => {}) {
    if (this.ocupado) return;
    this.ocupado = true;
    this.error = '';
    this.aviso = '';
    try {
      const { revision, medicion } = await pedirJSON(this.url, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ video: this.datos.video, pasada: this.fuente.pasada, rev: this.datos.rev, ...orden }),
      });
      this.datos = revision;
      this.medicion = medicion;
      this.aviso = 'Guardado en este equipo.';
      despues();
    } catch (e) {
      this.error = e.message;
    } finally {
      this.ocupado = false;
      this.refrescar();
    }
  }

  estado(e) {
    return this.datos.decisiones[this.fuente.pasada]?.marcas[e.id]?.estado || 'pendiente';
  }

  mostrar() {
    if (this.rep.enDirecto) return [el('p', { clase: 'sub', texto: 'La revisión estará disponible cuando termine el análisis.' })];
    if (!this.datos) {
      if (!this.ocupado && !this.error) this.cargar();
      return this.error ? [el('p', { clase: 'error', texto: this.error }), boton('Volver a cargar', () => this.cargar())]
        : [el('p', { clase: 'sub', texto: 'Abriendo la revisión de este vídeo…' })];
    }
    const todos = this.fuente.candidatos.filter((e) => e.tipo === this.tipo);
    const pendientes = todos.filter((e) => this.estado(e) === 'pendiente');
    const visibles = this.pendientes ? pendientes : todos;
    const elegido = visibles.find((e) => e.id === this.elegido) || visibles[0];
    return [el('section', { clase: 'revision-eventos', 'aria-label': 'Revisar acciones' },
      el('div', { clase: 'revision-cabecera' }, el('h2', { texto: 'Revisión de acciones' }), boton('Exportar JSON', () => this.exportar())),
      el('label', { clase: 'revision-fila' }, 'Tipo de acción', el('select', {
        onchange: (e) => { this.tipo = e.target.value; this.elegido = null; this.nota = null; this.refrescar(); },
      }, Object.entries(TIPOS).map(([valor, texto]) => el('option', { value: valor, selected: valor === this.tipo, texto })))),
      this.error ? el('div', { role: 'alert', clase: 'revision-fila' }, el('span', { clase: 'error', texto: this.error }), boton('Actualizar', () => this.cargar())) : null,
      el('div', { clase: 'sub', role: 'status', texto: this.aviso }),
      this.candidatos(elegido, visibles, pendientes.length, todos.length),
      this.anotaciones(),
      this.resultados(),
    )];
  }

  candidatos(elegido, visibles, pendientes, total) {
    const ver = (evento) => this.acciones.ver(Math.max(this.rep.inicio, evento.t - 5));
    const cambio = (e) => {
      this.elegido = Number(e.target.value);
      this.nota = null;
      this.refrescar();
    };
    const estado = elegido && this.estado(elegido);
    const marca = elegido && this.datos.decisiones[this.fuente.pasada]?.marcas[elegido.id];
    return el('section', { clase: 'revision-bloque' },
      el('h3', { texto: 'Propuestas de Ojeador' }),
      el('div', { clase: 'revision-fila' },
        el('span', { clase: 'sub', texto: `${pendientes} pendientes de ${total}` }),
        el('label', {}, el('input', { type: 'checkbox', checked: this.pendientes, onchange: (e) => { this.pendientes = e.target.checked; this.nota = null; this.refrescar(); } }), ' Solo pendientes'),
      ),
      elegido ? [
        el('select', { 'aria-label': 'Acción propuesta', onchange: cambio }, visibles.map((e) => el('option', {
          value: String(e.id), selected: e.id === elegido.id, texto: `${minutoPreciso(e.t)} · ${ESTADOS[this.estado(e)]}`,
        }))),
        el('div', { clase: 'sub', texto: [this.rep.equipos[elegido.equipo]?.nombre, elegido.detalle].filter(Boolean).join(' · ') }),
        el('div', { clase: 'revision-fila' }, boton('Ver desde 5 s antes', () => ver(elegido), 'primario'),
          boton('Ir a la propuesta', () => this.acciones.saltar(elegido.t))),
        el('input', { type: 'text', maxlength: '500', placeholder: 'Motivo opcional (centro, despeje, duplicada…)', 'aria-label': 'Motivo del veredicto',
          value: this.nota ?? marca?.nota ?? '', oninput: (e) => { this.nota = e.target.value; } }),
        el('div', { clase: 'revision-fila' }, ...['real', 'falsa', 'dudosa', ...(estado !== 'pendiente' ? ['pendiente'] : [])].map((resultado) =>
          boton(resultado === 'pendiente' ? 'Deshacer veredicto' : ESTADOS[resultado], () => {
            const nota = this.nota ?? marca?.nota ?? '';
            this.editar({ accion: 'decidir', id: elegido.id, estado: resultado, nota }, () => {
              this.nota = null;
              if (this.pendientes) this.elegido = null;
            });
          }, estado === resultado ? 'marcada' : ''))),
      ] : el('p', { clase: 'sub', texto: total ? 'Has revisado todas las propuestas de este tipo.' : 'Ojeador no propuso acciones de este tipo.' }),
    );
  }

  resultados() {
    const m = this.medicion[this.tipo];
    if (!m.segundos_revisados) return null;
    const pct = (p) => p == null ? '—' : `${numero(p, 1)} %`;
    return el('section', { clase: 'revision-bloque' },
      el('h3', { texto: 'Resultado en los tramos revisados' }),
      el('span', { clase: 'sub', texto: `${duracion(m.segundos_revisados)} evaluados${m.segundos_excluidos_por_duda ? ` · ${duracion(m.segundos_excluidos_por_duda)} excluidos por duda` : ''}` }),
      el('span', { texto: `${m.aciertos} aciertos · ${m.falsas} falsas · ${m.omitidas} omitidas` }),
      el('span', { texto: `Precisión ${pct(m.precision_pct)} · Cobertura ${pct(m.cobertura_pct)}` }),
    );
  }

  anotaciones() {
    const t = () => this.acciones.segundo();
    const hechos = this.datos.hechos.filter((h) => h.tipo === this.tipo && this.fuente.desde <= h.t && h.t <= this.fuente.hasta).sort((a, b) => a.t - b.t);
    const tramos = this.datos.tramos.filter((r) => r.tipo === this.tipo && r.desde < this.fuente.hasta && this.fuente.desde < r.hasta);
    const inicio = this.inicios[this.tipo];
    return el('section', { clase: 'revision-bloque' },
      el('h3', { texto: 'Lo que ocurrió en el vídeo' }),
      el('div', { clase: 'sub', texto: 'Pausa el vídeo en una acción omitida o de otro tipo y anótala en ese segundo.' }),
      boton('Anotar acción real aquí', () => this.editar({ accion: 'anotar', tipo: this.tipo, t: t() })),
      el('details', {}, el('summary', { texto: `${hechos.length} acciones reales en el tramo` }),
        hechos.map((h) => el('div', { clase: 'revision-fila' }, boton(minutoPreciso(h.t), () => this.acciones.saltar(h.t)),
          h.nota ? el('span', { clase: 'sub', texto: h.nota }) : null,
          boton('Quitar', () => this.editar({ accion: 'quitar_hecho', id: h.id }))))),
      el('h3', { texto: 'Tramos revisados de principio a fin' }),
      el('div', { clase: 'sub', texto: 'Marca un inicio y un final solo si has anotado todas las acciones de este tipo que se ven entre ambos. Así también se pueden medir las omisiones.' }),
      el('div', { clase: 'revision-fila' },
        boton(inicio == null ? 'Marcar inicio aquí' : `Inicio ${minutoPreciso(inicio)} · cambiar`, () => { this.inicios[this.tipo] = t(); this.refrescar(); }),
        inicio != null ? boton('Guardar final aquí', () => {
          const tipo = this.tipo;
          this.editar({ accion: 'cubrir', tipo, desde: inicio, hasta: t() }, () => { delete this.inicios[tipo]; });
        }) : null,
      ),
      tramos.map((r) => el('div', { clase: 'revision-fila' }, el('span', { texto: `${minutoPreciso(r.desde)} – ${minutoPreciso(r.hasta)}` }),
        boton('Quitar tramo', () => this.editar({ accion: 'quitar_tramo', id: r.id })))),
    );
  }

  exportar() {
    const url = URL.createObjectURL(new Blob([JSON.stringify(this.datos, null, 2)], { type: 'application/json' }));
    const a = el('a', { href: url, download: `revision-ojeador-${this.datos.video.slice(0, 12)}.json` });
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
