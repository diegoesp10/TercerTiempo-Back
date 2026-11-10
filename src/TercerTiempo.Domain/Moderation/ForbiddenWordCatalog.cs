namespace TercerTiempo.Domain.Moderation;

// Repertorio inicial de la tabla PalabrasProhibidas: palabrotas, insultos, vejaciones y amenazas del español de España y de
// América. Una palabra suelta vale también en plural, así que va en singular (y en femenino si se usa). Las palabras que
// además tienen un uso normal, sobre todo en el fútbol («pito», «pelota», «huevos», «robo» de balón, «basura», «perro»,
// «concha», «moro», «negro», «gitano»…), no van solas sino en la expresión que las hace ofensivas. El administrador puede
// añadir o quitar términos.
public static class ForbiddenWordCatalog
{
    public static readonly DateTimeOffset SeededAt = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Profanities =
    [
        // Mierda, joder, coño, hostia, cojones...
        "mierda", "mierdoso", "mierdosa", "mierdecilla", "puta mierda", "una mierda", "de mierda", "vete a la mierda",
        "joder", "jode", "jodido", "jodida", "jódete", "jodeos", "joderos", "jodienda", "que te jodan", "que os jodan",
        "coño", "coñazo", "hostia", "ostia", "hostiazo", "ostiazo", "hostión", "me cago en", "tus muertos",
        "cojón", "cojonudo", "cojonuda", "acojonante", "descojone", "de los cojones", "hasta los cojones",
        "hasta los huevos", "manda huevos", "tiene huevos", "por huevos", "cómeme los huevos", "chúpame los huevos",
        "huevón", "huevona", "carajo", "al carajo", "vete al carajo", "carajote",
        // Culo, polla, follar...
        "culo", "por culo", "tomar por culo", "a tomar por culo", "dar por culo", "que te den por culo", "ojete",
        "polla", "pollazo", "chupapollas", "comepollas", "chúpamela", "chúpala", "cómemela", "mamada", "mamar",
        "follar", "follado", "follada", "folla", "follando", "que te follen", "que os follen", "malfollado", "malfollada",
        "puta", "puto", "putada", "putear", "puteo", "putero", "putón", "putona", "putilla", "putas madres",
        "puta madre", "de puta madre", "me cago en la puta", "la puta que te parió", "la madre que te parió",
        "chocho", "chumino", "teta", "picha", "pajero", "pajillero", "hacerse una paja", "cagar", "cagada", "cagado",
        "cagarla", "cago", "cagón", "cagona",
        "me la suda", "me la sopla", "me la pela", "me la trae floja", "que te den", "que le den", "que os den",
        // América
        "verga", "vergazo", "chingar", "chinga", "chingada", "chingado", "chíngate", "chingadera", "pinche", "pendejada",
        "puñeta", "conchetumare", "conchatumadre", "conchesumadre", "concha de tu madre", "concha tu madre",
        "ctm", "ptm", "csm", "culiao", "culiado",
    ];

    private static readonly string[] Insults =
    [
        "gilipollas", "gilipolla", "jilipollas", "gilipuertas", "gilipollez", "imbécil", "idiota", "estúpido", "estúpida",
        "tonto", "tonta", "tontaina", "tontolaba", "tontolculo", "tonto del culo", "tontopolla", "bobo", "boba",
        "capullo", "capulla", "capullazo", "cabrón", "cabrona", "cabronazo", "cabronada", "mamón", "mamona", "mamonazo",
        "hijo de puta", "hija de puta", "hijos de puta", "hijas de puta", "hijo de la gran puta", "hijoputa", "hijaputa",
        "hijueputa", "hdp", "hdlgp", "tu puta madre", "me cago en tu madre",
        "soplapollas", "lameculos", "chupaculos", "comemierda", "pelotillero", "mamarracho", "mamarracha", "payaso",
        "payasa", "payasada", "bufón", "pringado", "pringada", "pringao", "panoli", "pardillo", "pardilla", "paleto",
        "cateto", "cateta", "palurdo", "palurda", "patán", "gañán", "garrulo", "garrula", "cenutrio", "cenutria",
        "berzotas", "zoquete", "zopenco", "zopenca", "mentecato", "mentecata", "majadero", "majadera", "botarate",
        "lerdo", "lerda", "cretino", "cretina", "tarugo", "cazurro", "cazurra", "merluzo", "mastuerzo", "cabestro",
        "lelo", "lela", "tarado", "tarada", "mequetrefe", "pelele", "cantamañanas", "soplagaitas", "abrazafarolas",
        "tolai", "bocazas", "bocachancla", "fantoche", "burro", "burra", "asno", "borrico",
        "cerdo", "cerda", "guarro", "guarra", "marrano", "marrana", "asqueroso", "asquerosa", "rata", "rastrero",
        "rastrera", "gusano", "escoria", "lacra", "miserable", "desgraciado", "desgraciada", "sinvergüenza", "caradura",
        "cornudo", "cornuda", "zorra", "zorrón", "furcia", "golfa", "ramera", "perra",
        // Contra el árbitro: insultos y acusaciones
        "ladrón", "ladrona", "chorizo", "atraco", "atracador", "corrupto", "corrupta", "mafioso", "tramposo", "tramposa",
        "estafador", "sobornado", "árbitro de mierda", "puto árbitro", "árbitro vendido", "estás vendido", "está vendido",
        "árbitro comprado", "estás comprado", "está comprado", "nos ha robado", "nos han robado", "nos robó",
        "robado el partido", "robo descarado", "árbitro ciego", "estás ciego", "está ciego", "cegato", "cegata",
        "árbitro inútil", "eres un inútil", "es un inútil", "inútil de mierda", "árbitro basura", "arbitraje basura",
        "eres basura", "es una basura", "eres una basura", "eres un anormal", "es un anormal", "vaya payaso",
        // América
        "pendejo", "pendeja", "boludo", "boluda", "pelotudo", "pelotuda", "huevón de mierda", "weón", "conchudo",
        "conchuda", "malparido", "malparida", "gonorrea", "mamaguevo", "mamahuevo", "carechimba", "cagón de mierda",
    ];

    private static readonly string[] Slurs =
    [
        "maricón", "maricona", "marica", "mariconazo", "mariconada", "bollera", "tortillera", "travelo",
        "julandrón", "mariposón", "nenaza", "joto",
        "subnormal", "retrasado mental", "retrasada mental", "retrasados mentales", "mongolo", "mongola", "mongólico",
        "mongólica", "deficiente mental",
        "sudaca", "panchito", "negrata", "negro de mierda", "moro de mierda", "moromierda", "gitano de mierda",
        "gitanada", "judío de mierda", "gordo de mierda", "gorda de mierda", "gordinflón", "gordinflona",
    ];

    private static readonly string[] Threats =
    [
        "te voy a matar", "te vamos a matar", "te mato", "os mato", "te mataré", "hay que matarlo", "hay que matarte",
        "que te maten", "que lo maten", "muérete", "ojalá te mueras", "ojalá te murieras", "ojalá te pase algo",
        "te parto la cara", "te partiré la cara", "partirte la cara", "te reviento", "te vamos a reventar",
        "te voy a pegar", "te vamos a pegar", "te vamos a esperar", "te esperamos fuera", "a la salida te espero",
        "sabemos dónde vives", "te vamos a romper las piernas", "romperte las piernas",
    ];

    public static IReadOnlyList<ForbiddenWord> All { get; } = Build();

    private static List<ForbiddenWord> Build()
    {
        var words = new Dictionary<string, ForbiddenWord>(StringComparer.Ordinal);
        void Add(IEnumerable<string> terms, ForbiddenWordKind kind)
        {
            foreach (var term in terms)
            {
                var word = ForbiddenWord.Create(term, kind, SeededAt);
                words.TryAdd(word.Term, word);
            }
        }

        Add(Threats, ForbiddenWordKind.Threat);
        Add(Slurs, ForbiddenWordKind.Slur);
        Add(Insults, ForbiddenWordKind.Insult);
        Add(Profanities, ForbiddenWordKind.Profanity);
        return words.Values.OrderBy(w => w.Term, StringComparer.Ordinal).ToList();
    }
}
