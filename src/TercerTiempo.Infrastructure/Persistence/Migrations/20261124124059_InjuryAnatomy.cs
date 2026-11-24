using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InjuryAnatomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Grade",
                table: "PlayerInjuries",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mechanism",
                table: "PlayerInjuries",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Side",
                table: "PlayerInjuries",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteCode",
                table: "PlayerInjuries",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseSiteCode",
                table: "InjuryTypes",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Graded",
                table: "InjuryTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SiteCodes",
                table: "InjuryTypes",
                type: "nvarchar(2500)",
                maxLength: 2500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SiteKind",
                table: "InjuryTypes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BodySites",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ParentCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Paired = table.Column<bool>(type: "bit", nullable: false),
                    Agreement = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodySites", x => x.Code);
                    table.ForeignKey(
                        name: "FK_BodySites_BodySites_ParentCode",
                        column: x => x.ParentCode,
                        principalTable: "BodySites",
                        principalColumn: "Code");
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "body_site.duplicate", "es", "Ya hay un sitio del cuerpo llamado «{0}»." },
                    { "body_site.in_use", "es", "«{0}» se usa en {1}, así que no se puede borrar. Si no quieres que se ofrezca más, retíralo: lo guardado lo conserva." },
                    { "body_site.invalid", "es", "La clase, la zona o cómo se escribe el lado no son válidos: elígelos de la lista." },
                    { "body_site.not_found", "es", "El sitio del cuerpo '{0}' no existe." },
                    { "body_site.parent_invalid", "es", "El grupo tiene que ser otro sitio de la misma clase que no esté dentro de otro grupo, y un sitio que ya es grupo de otros no puede meterse en uno ni cambiar de clase (saca antes los suyos)." },
                    { "injury_type.sites_invalid", "es", "«{0}» no encaja con lo que pregunta este tipo: quítalo de la lista." },
                    { "injury_type.sites_too_many", "es", "Un tipo puede ofrecer como mucho {0} sitios del cuerpo concretos; si son más, deja la lista vacía y se ofrecen todos los de su clase." },
                    { "injury.detail_invalid", "es", "El lado, el grado o cómo se produjo no son válidos: elígelos de la lista." },
                    { "injury.side_required", "es", "Falta el lado de la lesión ({0}): izquierdo, derecho o los dos." },
                    { "injury.site_invalid", "es", "«{0}» no encaja con «{1}»: elige otra opción de la lista." },
                    { "injury.site_not_found", "es", "«{0}» no está en el catálogo de sitios del cuerpo." },
                    { "injury.site_required", "es", "Falta saber {0}: elige una opción de la lista." },
                    { "injury.site_retired", "es", "«{0}» ya no se usa para lesiones nuevas: elige otra opción de la lista." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_SiteCode",
                table: "PlayerInjuries",
                column: "SiteCode");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryTypes_BaseSiteCode",
                table: "InjuryTypes",
                column: "BaseSiteCode");

            migrationBuilder.CreateIndex(
                name: "IX_BodySites_Label",
                table: "BodySites",
                column: "Label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BodySites_ParentCode",
                table: "BodySites",
                column: "ParentCode");

            migrationBuilder.AddForeignKey(
                name: "FK_InjuryTypes_BodySites_BaseSiteCode",
                table: "InjuryTypes",
                column: "BaseSiteCode",
                principalTable: "BodySites",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerInjuries_BodySites_SiteCode",
                table: "PlayerInjuries",
                column: "SiteCode",
                principalTable: "BodySites",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            SeedAnatomy(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var added = string.Join(", ", Types.Where(t => t.New).Select(t => Text(t.Code)));
            migrationBuilder.Sql($"DELETE FROM InjuryTypes WHERE Code IN ({added}) AND Code NOT IN (SELECT Type FROM PlayerInjuries);");

            migrationBuilder.DropForeignKey(
                name: "FK_InjuryTypes_BodySites_BaseSiteCode",
                table: "InjuryTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerInjuries_BodySites_SiteCode",
                table: "PlayerInjuries");

            migrationBuilder.DropTable(
                name: "BodySites");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_SiteCode",
                table: "PlayerInjuries");

            migrationBuilder.DropIndex(
                name: "IX_InjuryTypes_BaseSiteCode",
                table: "InjuryTypes");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "body_site.duplicate", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "body_site.in_use", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "body_site.invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "body_site.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "body_site.parent_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_type.sites_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_type.sites_too_many", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.detail_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.side_required", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.site_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.site_not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.site_required", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.site_retired", "es" });

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "Mechanism",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "Side",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "SiteCode",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "BaseSiteCode",
                table: "InjuryTypes");

            migrationBuilder.DropColumn(
                name: "Graded",
                table: "InjuryTypes");

            migrationBuilder.DropColumn(
                name: "SiteCodes",
                table: "InjuryTypes");

            migrationBuilder.DropColumn(
                name: "SiteKind",
                table: "InjuryTypes");
        }

        private static readonly DateTimeOffset SeededAt = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

        // El cuerpo, de la cabeza a los pies: código, nombre, clase, zona, grupo, si tiene lado y cómo concuerda el lado
        // (M «izquierdo», F «izquierda», MP «izquierdos», FP «izquierdas»; con la palabra a la que se refiere: «dedos de la
        // mano izquierda»). Nace aquí y después lo mantiene el administrador, así que no es HasData. Las pruebas lo leen de aquí
        // (internal) para probar las reglas y las sugerencias con el catálogo de verdad.
        internal static readonly (string Code, string Label, string Kind, string Region, string Parent, bool Paired, string Agreement)[] Sites =
        [
            ("Head", "Cabeza", "Zone", "Head", null, false, "F"),
            ("Face", "Cara", "Zone", "Head", null, false, "F"),
            ("Eye", "Ojo", "Zone", "Head", null, true, "M"),
            ("Nose", "Nariz", "Zone", "Head", null, false, "F"),
            ("Mouth", "Boca y dientes", "Zone", "Head", null, false, "F"),
            ("Ear", "Oreja", "Zone", "Head", null, true, "F"),
            ("Skull", "Cráneo", "Bone", "Head", null, false, "M"),
            ("NasalBones", "Huesos de la nariz", "Bone", "Head", null, false, "MP"),
            ("Cheekbone", "Pómulo", "Bone", "Head", null, true, "M"),
            ("Orbit", "Órbita", "Bone", "Head", null, true, "F"),
            ("Jaw", "Mandíbula", "Bone", "Head", null, false, "F"),

            ("Neck", "Cuello", "Zone", "Neck", null, false, "M"),
            ("CervicalVertebrae", "Vértebras cervicales", "Bone", "Neck", null, false, "FP"),
            ("Trapezius", "Trapecio", "Muscle", "Neck", null, true, "M"),
            ("Sternocleidomastoid", "Esternocleidomastoideo", "Muscle", "Neck", null, true, "M"),

            ("Shoulder", "Hombro", "Joint", "Shoulder", null, true, "M"),
            ("AcromioclavicularJoint", "Articulación acromioclavicular", "Joint", "Shoulder", null, true, "F"),
            ("Clavicle", "Clavícula", "Bone", "Shoulder", null, true, "F"),
            ("Scapula", "Escápula (omóplato)", "Bone", "Shoulder", null, true, "F"),
            ("Deltoid", "Deltoides", "Muscle", "Shoulder", null, true, "M"),
            ("RotatorCuff", "Manguito rotador", "Tendon", "Shoulder", null, true, "M"),

            ("Arm", "Brazo", "Zone", "Arm", null, true, "M"),
            ("Humerus", "Húmero", "Bone", "Arm", null, true, "M"),
            ("BicepsBrachii", "Bíceps braquial", "Muscle", "Arm", null, true, "M"),
            ("TricepsBrachii", "Tríceps braquial", "Muscle", "Arm", null, true, "M"),
            ("BicepsTendon", "Tendón del bíceps", "Tendon", "Arm", null, true, "M"),

            ("Elbow", "Codo", "Joint", "Elbow", null, true, "M"),
            ("EpicondyleTendons", "Tendones del epicóndilo", "Tendon", "Elbow", null, true, "M"),

            ("Forearm", "Antebrazo", "Zone", "Forearm", null, true, "M"),
            ("Radius", "Radio", "Bone", "Forearm", null, true, "M"),
            ("Ulna", "Cúbito", "Bone", "Forearm", null, true, "M"),
            ("ForearmFlexors", "Flexores del antebrazo", "Muscle", "Forearm", null, true, "M"),
            ("ForearmExtensors", "Extensores del antebrazo", "Muscle", "Forearm", null, true, "M"),

            ("Wrist", "Muñeca", "Joint", "Wrist", null, true, "F"),
            ("Scaphoid", "Escafoides", "Bone", "Wrist", null, true, "M"),
            ("WristTendons", "Tendones de la muñeca", "Tendon", "Wrist", null, true, "F"),

            ("Hand", "Mano", "Zone", "Hand", null, true, "F"),
            ("Fingers", "Dedos de la mano", "Joint", "Hand", null, true, "F"),
            ("Thumb", "Pulgar", "Joint", "Hand", null, true, "M"),
            ("Metacarpals", "Metacarpianos", "Bone", "Hand", null, true, "MP"),
            ("HandPhalanges", "Falanges de la mano", "Bone", "Hand", null, true, "F"),

            ("Chest", "Pecho", "Zone", "Chest", null, false, "M"),
            ("RibCage", "Parrilla costal", "Zone", "Chest", null, true, "F"),
            ("Ribs", "Costillas", "Bone", "Chest", null, true, "FP"),
            ("Sternum", "Esternón", "Bone", "Chest", null, false, "M"),
            ("Pectoral", "Pectoral", "Muscle", "Chest", null, true, "M"),
            ("Intercostals", "Intercostales", "Muscle", "Chest", null, true, "MP"),

            ("Abdomen", "Abdomen", "Zone", "Abdomen", null, false, "M"),
            ("Flank", "Costado", "Zone", "Abdomen", null, true, "M"),
            ("Abdominals", "Abdominales", "Muscle", "Abdomen", null, true, "MP"),
            ("RectusAbdominis", "Recto abdominal", "Muscle", "Abdomen", "Abdominals", true, "M"),
            ("Obliques", "Oblicuos", "Muscle", "Abdomen", "Abdominals", true, "MP"),

            ("UpperBack", "Espalda (zona dorsal)", "Zone", "Back", null, false, "F"),
            ("LowerBack", "Zona lumbar", "Zone", "Back", null, false, "F"),
            ("ThoracicVertebrae", "Vértebras dorsales", "Bone", "Back", null, false, "FP"),
            ("LumbarVertebrae", "Vértebras lumbares", "Bone", "Back", null, false, "FP"),
            ("Sacrum", "Sacro", "Bone", "Back", null, false, "M"),
            ("Coccyx", "Coxis", "Bone", "Back", null, false, "M"),
            ("SacroiliacJoint", "Articulación sacroilíaca", "Joint", "Back", null, true, "F"),
            ("Paravertebrals", "Paravertebrales lumbares", "Muscle", "Back", null, true, "MP"),
            ("QuadratusLumborum", "Cuadrado lumbar", "Muscle", "Back", null, true, "M"),
            ("LatissimusDorsi", "Dorsal ancho", "Muscle", "Back", null, true, "M"),
            ("Rhomboids", "Romboides", "Muscle", "Back", null, true, "MP"),

            ("Hip", "Cadera", "Joint", "Hip", null, true, "F"),
            ("Groin", "Ingle", "Zone", "Hip", null, true, "F"),
            ("Pubis", "Pubis", "Zone", "Hip", null, false, "M"),
            ("Buttock", "Glúteo", "Zone", "Hip", null, true, "M"),
            ("IliacCrest", "Cresta ilíaca", "Zone", "Hip", null, true, "F"),
            ("Genitals", "Zona genital", "Zone", "Hip", null, false, "F"),
            ("Pelvis", "Pelvis", "Bone", "Hip", null, false, "F"),
            ("Adductors", "Aductores", "Muscle", "Hip", null, true, "MP"),
            ("AdductorLongus", "Aductor largo", "Muscle", "Hip", "Adductors", true, "M"),
            ("AdductorBrevis", "Aductor corto", "Muscle", "Hip", "Adductors", true, "M"),
            ("AdductorMagnus", "Aductor mayor", "Muscle", "Hip", "Adductors", true, "M"),
            ("Gracilis", "Recto interno (grácil)", "Muscle", "Hip", "Adductors", true, "M"),
            ("Pectineus", "Pectíneo", "Muscle", "Hip", "Adductors", true, "M"),
            ("Glutes", "Glúteos", "Muscle", "Hip", null, true, "MP"),
            ("GluteusMaximus", "Glúteo mayor", "Muscle", "Hip", "Glutes", true, "M"),
            ("GluteusMedius", "Glúteo medio", "Muscle", "Hip", "Glutes", true, "M"),
            ("GluteusMinimus", "Glúteo menor", "Muscle", "Hip", "Glutes", true, "M"),
            ("Piriformis", "Piramidal (piriforme)", "Muscle", "Hip", null, true, "M"),
            ("Iliopsoas", "Psoas ilíaco", "Muscle", "Hip", null, true, "M"),
            ("TensorFasciaeLatae", "Tensor de la fascia lata", "Muscle", "Hip", null, true, "M"),
            ("AdductorTendons", "Tendones de los aductores", "Tendon", "Hip", null, true, "MP"),
            ("PsoasTendon", "Tendón del psoas", "Tendon", "Hip", null, true, "M"),

            ("Thigh", "Muslo", "Zone", "Thigh", null, true, "M"),
            ("Femur", "Fémur", "Bone", "Thigh", null, true, "M"),
            ("Hamstrings", "Isquiotibiales", "Muscle", "Thigh", null, true, "MP"),
            ("BicepsFemoris", "Bíceps femoral", "Muscle", "Thigh", "Hamstrings", true, "M"),
            ("Semitendinosus", "Semitendinoso", "Muscle", "Thigh", "Hamstrings", true, "M"),
            ("Semimembranosus", "Semimembranoso", "Muscle", "Thigh", "Hamstrings", true, "M"),
            ("Quadriceps", "Cuádriceps", "Muscle", "Thigh", null, true, "M"),
            ("RectusFemoris", "Recto anterior", "Muscle", "Thigh", "Quadriceps", true, "M"),
            ("VastusMedialis", "Vasto interno", "Muscle", "Thigh", "Quadriceps", true, "M"),
            ("VastusLateralis", "Vasto externo", "Muscle", "Thigh", "Quadriceps", true, "M"),
            ("VastusIntermedius", "Vasto intermedio (crural)", "Muscle", "Thigh", "Quadriceps", true, "M"),
            ("Sartorius", "Sartorio", "Muscle", "Thigh", null, true, "M"),
            ("IliotibialBand", "Cintilla iliotibial", "Tendon", "Thigh", null, true, "F"),
            ("HamstringTendons", "Tendones de los isquiotibiales", "Tendon", "Thigh", null, true, "MP"),

            ("Knee", "Rodilla", "Joint", "Knee", null, true, "F"),
            ("PatellofemoralJoint", "Articulación femoropatelar (rótula)", "Joint", "Knee", null, true, "F"),
            ("Patella", "Rótula", "Bone", "Knee", null, true, "F"),
            ("AnteriorCruciateLigament", "Ligamento cruzado anterior (LCA)", "Ligament", "Knee", null, true, "M"),
            ("PosteriorCruciateLigament", "Ligamento cruzado posterior (LCP)", "Ligament", "Knee", null, true, "M"),
            ("MedialCollateralLigament", "Ligamento lateral interno (LLI)", "Ligament", "Knee", null, true, "M"),
            ("LateralCollateralLigament", "Ligamento lateral externo (LLE)", "Ligament", "Knee", null, true, "M"),
            ("MedialMeniscus", "Menisco interno", "Cartilage", "Knee", null, true, "M"),
            ("LateralMeniscus", "Menisco externo", "Cartilage", "Knee", null, true, "M"),
            ("PatellarTendon", "Tendón rotuliano", "Tendon", "Knee", null, true, "M"),
            ("QuadricepsTendon", "Tendón del cuádriceps", "Tendon", "Knee", null, true, "M"),
            ("PesAnserinus", "Pata de ganso", "Tendon", "Knee", null, true, "F"),

            ("Shin", "Espinilla", "Zone", "Leg", null, true, "F"),
            ("Calf", "Pantorrilla", "Zone", "Leg", null, true, "F"),
            ("Tibia", "Tibia", "Bone", "Leg", null, true, "F"),
            ("Fibula", "Peroné", "Bone", "Leg", null, true, "M"),
            ("TricepsSurae", "Gemelos y sóleo", "Muscle", "Leg", null, true, "MP"),
            ("MedialGastrocnemius", "Gemelo interno", "Muscle", "Leg", "TricepsSurae", true, "M"),
            ("LateralGastrocnemius", "Gemelo externo", "Muscle", "Leg", "TricepsSurae", true, "M"),
            ("Soleus", "Sóleo", "Muscle", "Leg", "TricepsSurae", true, "M"),
            ("Plantaris", "Plantar delgado", "Muscle", "Leg", "TricepsSurae", true, "M"),
            ("TibialisAnterior", "Tibial anterior", "Muscle", "Leg", null, true, "M"),
            ("Peroneals", "Peroneos", "Muscle", "Leg", null, true, "MP"),
            ("AchillesTendon", "Tendón de Aquiles", "Tendon", "Leg", null, true, "M"),

            ("Ankle", "Tobillo", "Joint", "Ankle", null, true, "M"),
            ("Malleoli", "Maléolos del tobillo", "Bone", "Ankle", null, true, "M"),
            ("Talus", "Astrágalo", "Bone", "Ankle", null, true, "M"),
            ("AnteriorTalofibularLigament", "Ligamento peroneoastragalino anterior (LPAA)", "Ligament", "Ankle", null, true, "M"),
            ("CalcaneofibularLigament", "Ligamento peroneocalcáneo (LPC)", "Ligament", "Ankle", null, true, "M"),
            ("DeltoidLigament", "Ligamento deltoideo (interno)", "Ligament", "Ankle", null, true, "M"),
            ("Syndesmosis", "Sindesmosis (esguince alto)", "Ligament", "Ankle", null, true, "F"),
            ("PeronealTendons", "Tendones peroneos", "Tendon", "Ankle", null, true, "MP"),
            ("TibialisPosteriorTendon", "Tendón del tibial posterior", "Tendon", "Ankle", null, true, "M"),
            ("TibialisAnteriorTendon", "Tendón del tibial anterior", "Tendon", "Ankle", null, true, "M"),

            ("Foot", "Pie", "Zone", "Foot", null, true, "M"),
            ("Heel", "Talón", "Zone", "Foot", null, true, "M"),
            ("Sole", "Planta del pie", "Zone", "Foot", null, true, "M"),
            ("Toes", "Dedos del pie", "Joint", "Foot", null, true, "M"),
            ("BigToe", "Dedo gordo del pie", "Joint", "Foot", null, true, "M"),
            ("Calcaneus", "Calcáneo", "Bone", "Foot", null, true, "M"),
            ("Navicular", "Escafoides del pie", "Bone", "Foot", null, true, "M"),
            ("Metatarsals", "Metatarsianos", "Bone", "Foot", null, true, "MP"),
            ("FifthMetatarsal", "Quinto metatarsiano", "Bone", "Foot", null, true, "M"),
            ("FootPhalanges", "Falanges del pie", "Bone", "Foot", null, true, "M"),
            ("PlantarFascia", "Fascia plantar", "Tendon", "Foot", null, true, "F"),
        ];

        // Los tipos: código, nombre y duración media (de los nuevos; los que ya había conservan los suyos), orden, lo que
        // preguntan (una clase de sitio o un sitio fijo con lo que lo precisa), si llevan grado y si son nuevos.
        internal static readonly (string Code, string Label, int? Days, int Order, string Kind, string Base, string[] Sites, bool Graded, bool New)[] Types =
        [
            ("MuscleContracture", "Contractura muscular", 7, 1, "Muscle", null, [], false, false),
            ("MuscleOverload", "Sobrecarga muscular", 5, 2, "Muscle", null, [], false, true),
            ("MuscleStrain", "Elongación muscular", 10, 3, "Muscle", null, [], false, true),
            ("MuscleTear", "Rotura fibrilar", 21, 4, "Muscle", null, [], true, false),
            ("AnkleSprain", "Esguince de tobillo", 21, 5, null, "Ankle",
                ["AnteriorTalofibularLigament", "CalcaneofibularLigament", "DeltoidLigament", "Syndesmosis"], true, false),
            ("KneeSprain", "Esguince de rodilla", 28, 6, null, "Knee", ["MedialCollateralLigament", "LateralCollateralLigament"], true, false),
            ("Sprain", "Esguince (otras articulaciones)", 14, 7, "Joint", null,
                ["Shoulder", "AcromioclavicularJoint", "Elbow", "Wrist", "Fingers", "Thumb", "Hip", "Toes", "BigToe"], true, true),
            ("CruciateLigament", "Ligamento cruzado", 240, 8, null, "Knee", ["AnteriorCruciateLigament", "PosteriorCruciateLigament"], true, false),
            ("Meniscus", "Menisco", 60, 9, null, "Knee", ["MedialMeniscus", "LateralMeniscus"], false, false),
            ("Dislocation", "Luxación", 35, 10, "Joint", null, [], false, true),
            ("Fracture", "Fractura", 60, 11, "Bone", null, [], false, false),
            ("StressFracture", "Fractura por estrés", 70, 12, "Bone", null,
                ["Tibia", "Fibula", "Metatarsals", "FifthMetatarsal", "Navicular", "Calcaneus", "Femur", "Pelvis", "LumbarVertebrae"], false, true),
            ("Contusion", "Contusión / golpe", 5, 13, "Zone", null, [], false, false),
            ("Laceration", "Herida o brecha", 7, 14, "Zone", null, [], false, true),
            ("Tendinitis", "Tendinitis", 21, 15, "Tendon", null, [], false, false),
            ("TendonRupture", "Rotura de tendón", 180, 16, "Tendon", null, [], false, true),
            ("Periostitis", "Periostitis tibial", 21, 17, null, "Tibia", [], false, true),
            ("PlantarFasciitis", "Fascitis plantar", 42, 18, null, "PlantarFascia", [], false, true),
            ("Pubalgia", "Pubalgia", 42, 19, null, "Pubis", [], false, false),
            ("LowBackPain", "Lumbalgia", 10, 20, null, "LowerBack", [], false, true),
            ("Concussion", "Conmoción", 10, 21, null, "Head", [], false, false),
            ("Illness", "Enfermedad", 5, 22, null, null, [], false, false),
            ("Other", "Otra", null, 99, "Zone", null, [], false, false),
        ];

        private static string Text(string value) => value is null ? "NULL" : $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";

        // Como SQL dinámico: se compila al ejecutarse, después de crear las columnas nuevas (el script de la migración va
        // en un solo bloque y, si no, no las encontraría).
        private static string Exec(string sql) => $"EXEC(N'{sql.Replace("'", "''", StringComparison.Ordinal)}');";

        internal static string AgreementOf(string code) => code switch
        {
            "F" => "Feminine",
            "MP" => "MasculinePlural",
            "FP" => "FemininePlural",
            _ => "Masculine",
        };

        private static void SeedAnatomy(MigrationBuilder migrationBuilder)
        {
            // Primero los grupos y los sitios sueltos; después los que van dentro de un grupo (la clave ajena del grupo).
            var columns = new[] { "Code", "Label", "Kind", "Region", "ParentCode", "Paired", "Agreement", "SortOrder", "Active", "UpdatedAt" };
            var ordered = Sites.Select((site, index) => (Site: site, Order: (index + 1) * 10)).ToList();
            foreach (var batch in new[] { ordered.Where(r => r.Site.Parent is null).ToList(), ordered.Where(r => r.Site.Parent is not null).ToList() })
            {
                var values = new object[batch.Count, columns.Length];
                for (var i = 0; i < batch.Count; i++)
                {
                    var (site, order) = batch[i];
                    object[] row = [site.Code, site.Label, site.Kind, site.Region, site.Parent, site.Paired, AgreementOf(site.Agreement), order, true, SeededAt];
                    for (var j = 0; j < row.Length; j++) values[i, j] = row[j];
                }
                migrationBuilder.InsertData("BodySites", columns, values);
            }

            // Los tipos nuevos entran si no hay ya uno con ese código o ese nombre (el administrador pudo crearlo); todos
            // los del catálogo, nuevos o de siempre, pasan a preguntar lo suyo. Los creados por el administrador no se tocan.
            const string stamp = "2026-11-24T00:00:00+00:00";
            foreach (var t in Types)
            {
                var sites = Text(string.Join(',', t.Sites));
                migrationBuilder.Sql(Exec($"""
                    IF NOT EXISTS (SELECT 1 FROM InjuryTypes WHERE Code = {Text(t.Code)} OR Label = {Text(t.Label)})
                        INSERT INTO InjuryTypes (Code, Label, AverageDays, SortOrder, Active, UpdatedAt, SiteKind, BaseSiteCode, SiteCodes, Graded)
                        VALUES ({Text(t.Code)}, {Text(t.Label)}, {t.Days?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"}, {t.Order}, 1, '{stamp}', NULL, NULL, N'', 0);
                    UPDATE InjuryTypes
                    SET SortOrder = {t.Order}, SiteKind = {Text(t.Kind)}, BaseSiteCode = {Text(t.Base)}, SiteCodes = {sites}, Graded = {(t.Graded ? 1 : 0)}, UpdatedAt = '{stamp}'
                    WHERE Code = {Text(t.Code)};
                    """));
            }

            // La «Luxación de codo» que se creó a mano mientras el catálogo era genérico pasa a ser una luxación en el codo
            // (el lado no se sabía) y, ya sin lesiones, sale del catálogo.
            var seeded = string.Join(", ", Types.Select(t => Text(t.Code)));
            migrationBuilder.Sql(Exec($"""
                IF EXISTS (SELECT 1 FROM InjuryTypes WHERE Code = N'Dislocation')
                BEGIN
                    UPDATE i SET i.Type = N'Dislocation', i.SiteCode = N'Elbow'
                    FROM PlayerInjuries i JOIN InjuryTypes t ON t.Code = i.Type
                    WHERE t.Code NOT IN ({seeded}) AND t.Label LIKE N'%luxaci%codo%';
                    DELETE FROM InjuryTypes
                    WHERE Code NOT IN ({seeded}) AND Label LIKE N'%luxaci%codo%' AND Code NOT IN (SELECT Type FROM PlayerInjuries);
                END
                """));

            // Las lesiones ya guardadas de un tipo con sitio fijo (un esguince de tobillo) ya saben dónde fueron; el lado,
            // no: se pregunta al corregirlas.
            migrationBuilder.Sql(Exec("""
                UPDATE i SET i.SiteCode = t.BaseSiteCode
                FROM PlayerInjuries i JOIN InjuryTypes t ON t.Code = i.Type
                WHERE i.SiteCode IS NULL AND t.BaseSiteCode IS NOT NULL;
                """));
        }
    }
}
