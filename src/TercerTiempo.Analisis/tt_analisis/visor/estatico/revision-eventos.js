// Lo que de verdad pasa en el vídeo, anotado a mano, para medir lo que propone el Ojeador sin tocar su propuesta: las
// jugadas y las acciones de cada jugador que van al acta. Los tipos, los motivos y el segundo de cada tipo los da el
// servidor (`revision_eventos.py`).
import { nombreCorto, pedirJSON } from './datos.js';
import { duracion, el, minutoPreciso, numero } from './util.js';

const ESTADOS = { real: 'Real', falsa: 'Falsa', dudosa: 'No se ve', pendiente: 'Pendiente' };
const QUIEN = { bien: 'fue él', otro: 'fue otro', no_se_sabe: 'no se ve quién' };
const ORIGEN = { manual: 'Anotada a mano', confirmada: 'En el segundo de la propuesta', exacta: 'En su segundo exacto' };
const JUGADOR = { bien: 'bien', mal: 'mal', sin_identificar: 'sin identificar', sin_saber: 'sin saber quién fue' };
const boton = (texto, pulsar, clase = '', titulo = null) => el('button', { type: 'button', clase: `boton ${clase}`, texto, title: titulo, onclick: pulsar });
const contar = (n, uno, varios) => `${n} ${n === 1 ? uno : varios}`;
const claveDe = (j) => `${j.equipo}|${j.dorsal}`;

export class RevisionEventos {
  constructor(rep, acciones, refrescar) {
    this.url = `/api/a/${rep.id}/revision-eventos`;
    this.rep = rep;
    this.acciones = acciones;
    this.refrescar = refrescar;
    this.tipo = 'tiro';
    this.pendientes = true;
    // La propuesta elegida, por su id como texto (las jugadas tienen un número y las acciones un texto).
    this.elegido = null;
    this.inicios = {};
    this.datos = null;
    this.ocupado = false;
    this.error = '';
    this.aviso = '';
    this.limpiar();
    this.quienAnotar = '';
  }

  limpiar() {
    this.nota = null;
    // Sin tocar (null), lo que ya tenía la propuesta; '' es «Sin motivo».
    this.motivo = null;
    this.quien = null;
  }

  async cargar() {
    if (this.ocupado) return;
    this.error = '';
    this.ocupado = true;
    try {
      const { fuente, catalogo, revision, medicion } = await pedirJSON(this.url);
      this.fuente = fuente;
      this.catalogo = catalogo;
      this.datos = revision;
      this.medicion = medicion;
      this.porClave = new Map(fuente.jugadores.map((j) => [claveDe(j), j]));
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
      const respuesta = await pedirJSON(this.url, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ video: this.datos.video, pasada: this.fuente.pasada, rev: this.datos.rev, ...orden }),
      });
      this.datos = respuesta.revision;
      this.medicion = respuesta.medicion;
      this.aviso = respuesta.aviso || 'Guardado en este equipo.';
      despues();
    } catch (e) {
      this.error = e.message;
    } finally {
      this.ocupado = false;
      this.refrescar();
    }
  }

  marca(e) {
    return this.datos.decisiones[this.fuente.pasada]?.marcas[String(e.id)];
  }

  estado(e) {
    return this.marca(e)?.estado || 'pendiente';
  }

  esAccion(tipo = this.tipo) {
    return this.catalogo.acciones.includes(tipo);
  }

  textoJugador(j) {
    if (!j) return 'Sin identificar';
    return `${j.dorsal}${j.nombre ? ` ${nombreCorto(j.nombre)}` : ''} (${j.equipo})`;
  }

  // La primera que queda desde donde está el vídeo: al marcar una, se pasa a la siguiente.
  elegir(visibles) {
    const elegido = visibles.find((e) => String(e.id) === this.elegido);
    if (elegido) return elegido;
    const ahora = this.acciones.segundo();
    return visibles.find((e) => e.t >= ahora - 1) || visibles[0] || null;
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
    const margen = this.catalogo.margenes[this.tipo];
    return [el('section', { clase: 'revision-eventos', 'aria-label': 'Revisar lo que propone el Ojeador' },
      el('div', { clase: 'revision-cabecera' }, el('h2', { texto: 'Revisión' }), boton('Exportar JSON', () => this.exportar())),
      this.selectorTipo(),
      el('div', { clase: 'sub', texto: `${this.catalogo.pistas[this.tipo]} Se da por buena la propuesta a ${numero(margen)} s o menos.` }),
      this.error ? el('div', { role: 'alert', clase: 'revision-fila' }, el('span', { clase: 'error', texto: this.error }), boton('Actualizar', () => this.cargar())) : null,
      el('div', { clase: 'sub', role: 'status', texto: this.aviso }),
      this.propuestas(this.elegir(visibles), visibles, pendientes.length, todos.length),
      this.anotaciones(),
      this.resultados(),
    )];
  }

  selectorTipo() {
    const grupo = (etiqueta, tipos) => el('optgroup', { label: etiqueta },
      tipos.map((t) => el('option', { value: t, selected: t === this.tipo, texto: this.catalogo.tipos[t] })));
    return el('label', { clase: 'revision-fila' }, 'Qué revisar', el('select', {
      onchange: (e) => { this.tipo = e.target.value; this.elegido = null; this.limpiar(); this.refrescar(); },
    }, grupo('Jugadas', this.catalogo.jugadas), grupo('Acciones del acta', this.catalogo.acciones)));
  }

  // El hecho al que quedó enlazada una propuesta marcada como real (con quién la hizo, si se sabe).
  hechoDe(marca) {
    return marca?.hecho ? this.datos.hechos.find((h) => h.id === marca.hecho) : null;
  }

  textoClave(clave) {
    return this.textoJugador(this.porClave.get(claveDe(clave)) || clave);
  }

  // Quién hizo la acción, como se elige en la lista: «bien», «otro», «no_se_sabe», «otro:equipo|dorsal» o «» (sin
  // decirlo: lo anotado antes no cambia). De partida, lo que ya se marcó; si no, que fue el que dice la propuesta.
  quienElegido(e) {
    if (this.quien?.id === String(e.id)) return this.quien.valor;
    const marca = this.marca(e);
    const hecho = marca?.quien ? this.hechoDe(marca) : null;
    if (hecho) {
      if (hecho.jugador) return e.jugador && claveDe(hecho.jugador) === claveDe(e.jugador) ? 'bien' : `otro:${claveDe(hecho.jugador)}`;
      return marca.quien === 'otro' && e.jugador ? 'otro' : 'no_se_sabe';
    }
    return e.jugador ? 'bien' : '';
  }

  quienParaEnviar(e) {
    if (!this.esAccion(e.tipo)) return {};
    const valor = this.quienElegido(e);
    if (!valor) return {};
    if (!valor.startsWith('otro:')) return { quien: valor };
    const [equipo, dorsal] = [valor.slice(5, valor.lastIndexOf('|')), Number(valor.slice(valor.lastIndexOf('|') + 1))];
    return { quien: 'otro', jugador: { equipo, dorsal } };
  }

  selectorQuien(e) {
    const suyo = e.jugador ? claveDe(e.jugador) : null;
    const elegido = this.quienElegido(e);
    const opciones = [
      e.jugador ? el('option', { value: 'bien', texto: `Fue él: ${this.textoJugador(e.jugador)}` }) : el('option', { value: '', texto: 'Sin decir quién' }),
      ...this.fuente.jugadores.filter((j) => claveDe(j) !== suyo).map((j) => el('option', { value: `otro:${claveDe(j)}`, texto: `Fue ${this.textoJugador(j)}` })),
      e.jugador ? el('option', { value: 'otro', texto: 'Fue otro, no sé quién' }) : null,
      el('option', { value: 'no_se_sabe', texto: 'No se ve quién' }),
    ];
    // Uno anotado en otra pasada que en esta no está identificado.
    if (elegido.startsWith('otro:') && !this.porClave.has(elegido.slice(5))) {
      const [equipo, dorsal] = [elegido.slice(5, elegido.lastIndexOf('|')), elegido.slice(elegido.lastIndexOf('|') + 1)];
      opciones.splice(1, 0, el('option', { value: elegido, texto: `Fue ${dorsal} (${equipo})` }));
    }
    for (const o of opciones) if (o && o.value === elegido) o.selected = true;
    return el('label', { clase: 'revision-fila' }, 'Quién la hizo', el('select', {
      'aria-label': 'Quién hizo la acción',
      // Sin el foco: con él, las flechas para mover el vídeo cambiarían el jugador elegido sin que se note.
      onchange: (ev) => { this.quien = { id: String(e.id), valor: ev.target.value }; ev.target.blur(); },
    }, opciones));
  }

  propuestas(elegido, visibles, pendientes, total) {
    const ver = (evento) => this.acciones.ver(Math.max(this.rep.inicio, evento.t - 5));
    const marca = elegido && this.marca(elegido);
    const estado = elegido ? this.estado(elegido) : 'pendiente';
    const decidir = (resultado, extra = {}) => this.editar(
      { accion: 'decidir', id: elegido.id, estado: resultado, nota: this.nota ?? marca?.nota ?? '', ...extra },
      () => { this.limpiar(); if (this.pendientes) this.elegido = null; },
    );
    const equipo = (e) => this.fuente.equipos[String(e.equipo)];
    const quien = (e) => (!this.esAccion(e.tipo) ? equipo(e) : e.jugador ? this.textoJugador(e.jugador) : `Sin identificar${equipo(e) ? ` (${equipo(e)})` : ''}`);
    const hecho = marca?.quien ? this.hechoDe(marca) : null;
    const quienFue = hecho?.jugador ? `fue ${this.textoClave(hecho.jugador)}` : marca?.quien ? QUIEN[marca.quien] : null;
    const ahora = marca ? [ESTADOS[marca.estado], quienFue, marca.motivo ? this.catalogo.motivos[marca.motivo] : null].filter(Boolean).join(' · ') : null;
    const motivo = this.motivo ?? marca?.motivo ?? '';
    return el('section', { clase: 'revision-bloque' },
      el('h3', { texto: 'Lo que propone el Ojeador' }),
      el('div', { clase: 'revision-fila' },
        el('span', { clase: 'sub', texto: `${pendientes} sin revisar de ${total}` }),
        el('label', {}, el('input', { type: 'checkbox', checked: this.pendientes, onchange: (e) => { this.pendientes = e.target.checked; this.elegido = null; this.limpiar(); this.refrescar(); } }), ' Solo sin revisar'),
      ),
      elegido ? [
        el('select', {
          'aria-label': 'Propuesta',
          onchange: (e) => { this.elegido = e.target.value; this.limpiar(); this.refrescar(); },
        }, visibles.map((e) => el('option', {
          value: String(e.id), selected: e.id === elegido.id, texto: `${minutoPreciso(e.t)} · ${quien(e) || ''} · ${ESTADOS[this.estado(e)]}`,
        }))),
        el('div', { clase: 'sub', texto: [quien(elegido), elegido.detalle].filter(Boolean).join(' · ') }),
        ahora ? el('div', { clase: 'sub', texto: `Ahora: ${ahora}` }) : null,
        el('div', { clase: 'revision-fila' }, boton('Ver desde 5 s antes', () => ver(elegido), 'primario'),
          boton('Ir a la propuesta', () => this.acciones.saltar(elegido.t))),
        this.esAccion(elegido.tipo) ? this.selectorQuien(elegido) : null,
        el('label', { clase: 'revision-fila' }, 'Si es falsa, por qué', el('select', {
          'aria-label': 'Motivo de la falsa',
          onchange: (e) => { this.motivo = e.target.value; e.target.blur(); },
        }, [el('option', { value: '', texto: 'Sin motivo' }),
          ...Object.entries(this.catalogo.motivos).map(([valor, texto]) => el('option', { value: valor, selected: valor === motivo, texto }))])),
        el('input', {
          type: 'text', maxlength: '500', placeholder: 'Nota opcional', 'aria-label': 'Nota del veredicto',
          value: this.nota ?? marca?.nota ?? '', oninput: (e) => { this.nota = e.target.value; },
        }),
        el('div', { clase: 'revision-fila' },
          boton('Real', () => decidir('real', this.quienParaEnviar(elegido)), estado === 'real' ? 'marcada' : '', 'Pasó, y la propuesta está en su segundo (o a menos del margen).'),
          boton('Real en este segundo', () => decidir('real', { ...this.quienParaEnviar(elegido), t: this.acciones.segundo() }), '',
            'Para el vídeo en el momento exacto de la acción y púlsalo: se guarda ese segundo, que es más justo para medir las versiones siguientes.'),
          boton('Falsa', () => { const m = this.motivo ?? motivo; decidir('falsa', m ? { motivo: m } : {}); }, estado === 'falsa' ? 'marcada' : ''),
          boton('No se ve', () => decidir('dudosa'), estado === 'dudosa' ? 'marcada' : '', 'No se puede saber si pasó: ni esta propuesta ni lo que haya cerca cuentan al medir.'),
          estado !== 'pendiente' ? boton('Deshacer veredicto', () => decidir('pendiente')) : null),
      ] : el('p', { clase: 'sub', texto: total ? 'Has revisado todas las propuestas de este tipo.' : 'El Ojeador no propuso nada de este tipo.' }),
    );
  }

  anotaciones() {
    const t = () => this.acciones.segundo();
    const accion = this.esAccion();
    const hechos = this.datos.hechos.filter((h) => h.tipo === this.tipo && this.fuente.desde <= h.t && h.t <= this.fuente.hasta).sort((a, b) => a.t - b.t);
    const tramos = this.datos.tramos.filter((r) => r.tipo === this.tipo && r.desde < this.fuente.hasta && this.fuente.desde < r.hasta);
    const inicio = this.inicios[this.tipo];
    const anotar = () => {
      const j = this.porClave.get(this.quienAnotar);
      this.editar({ accion: 'anotar', tipo: this.tipo, t: t(), ...(accion && j ? { jugador: { equipo: j.equipo, dorsal: j.dorsal } } : {}) });
    };
    const quienFue = (h) => (!accion ? null : h.jugador ? this.textoClave(h.jugador) : 'no se sabe quién');
    return el('section', { clase: 'revision-bloque' },
      el('h3', { texto: 'Lo que pasó en el vídeo' }),
      el('div', { clase: 'sub', texto: 'Para el vídeo en lo que se le escapó al Ojeador (o en lo que propuso con otro tipo) y anótalo en ese segundo.' }),
      accion ? el('label', { clase: 'revision-fila' }, 'Quién la hizo', el('select', {
        'aria-label': 'Quién hizo la acción anotada',
        onchange: (e) => { this.quienAnotar = e.target.value; e.target.blur(); },
      }, [el('option', { value: '', texto: 'No se ve quién' }),
        ...this.fuente.jugadores.map((j) => el('option', { value: claveDe(j), selected: claveDe(j) === this.quienAnotar, texto: this.textoJugador(j) }))])) : null,
      boton('Anotar aquí lo que pasó', anotar),
      el('details', {}, el('summary', { texto: `${contar(hechos.length, 'cosa anotada', 'cosas anotadas')} de este tipo` }),
        hechos.map((h) => el('div', { clase: 'revision-fila' }, boton(minutoPreciso(h.t), () => this.acciones.saltar(h.t), '', ORIGEN[h.origen]),
          quienFue(h) ? el('span', { clase: 'sub', texto: quienFue(h) }) : null,
          h.nota ? el('span', { clase: 'sub', texto: h.nota }) : null,
          boton('Quitar', () => this.editar({ accion: 'quitar_hecho', id: h.id }))))),
      el('h3', { texto: 'Tramos revisados de principio a fin' }),
      el('div', { clase: 'sub', texto: 'Marca un inicio y un final solo si has anotado todo lo de este tipo que pasa entre ambos. Así también se miden las que se le escapan.' }),
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

  resultados() {
    const m = this.medicion[this.tipo];
    if (!m || !m.segundos_revisados) return null;
    const pct = (p) => (p == null ? '—' : `${numero(p, 1)} %`);
    const nombres = { ...this.catalogo.motivos, sin_motivo: 'Sin motivo', sin_revisar: 'Sin veredicto en esta pasada' };
    const motivos = Object.entries(m.motivos).sort((a, b) => b[1] - a[1]).map(([k, n]) => `${n} ${nombres[k].toLowerCase()}`).join(', ');
    const j = m.jugador;
    return el('section', { clase: 'revision-bloque' },
      el('h3', { texto: 'Resultado en los tramos revisados' }),
      el('span', { clase: 'sub', texto: `${duracion(m.segundos_revisados)} evaluados${m.segundos_excluidos_por_duda ? ` · ${duracion(m.segundos_excluidos_por_duda)} fuera por dudosos` : ''}` }),
      el('span', { texto: `${contar(m.aciertos, 'acierto', 'aciertos')} · ${contar(m.falsas, 'falsa', 'falsas')} · ${contar(m.omitidas, 'omitida', 'omitidas')}` }),
      el('span', { texto: `Precisión ${pct(m.precision_pct)} · Cobertura ${pct(m.cobertura_pct)}` }),
      j && m.aciertos ? el('span', { texto: `Quién la hizo: ${j.bien} ${JUGADOR.bien} y ${j.mal} ${JUGADOR.mal}${j.acierto_pct != null ? ` (${pct(j.acierto_pct)} bien)` : ''} · ${j.sin_identificar} ${JUGADOR.sin_identificar} · ${j.sin_saber} ${JUGADOR.sin_saber}` }) : null,
      motivos ? el('span', { clase: 'sub', texto: `Por qué son falsas: ${motivos}.` }) : null,
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
