using UnityEngine;

// Carte de l'étage 1 du campus SUPINFO Lille (scène Etage1), d'après le plan plan/Sans titrqqqqe.png.
// Positions du plan relevées à ~0,12 m par pixel du dessin puis multipliées par F1Scale (0,5 : étage deux fois plus petit) ;
// la cage d'escalier garde la taille de l'escalier du rez-de-chaussée. Nord du plan = +Z, 1 unité = 1 m.
//
//  60   ┌──────────┬─────┬───────────────┬───────────────────────────┬──────────────┐
//       │ (fermé)  │     │               │[]T     Salle S1.5         │              │
//  44   ├──────[]──┘     │               ├───────────────────────────┤              │
//       │ Salle S1.4 (↓) │  espace       │ T      Salle S1.2 (←)     │  Salle S1.1  │
//  24   ├───────T────────┤  ouvert       │[]                         │     (↓)      │
//       │ Salle S1.3 (↑) │               ├────────┬──────┬───────────┤              │
//       │                │               │ WC     │ asc. │    WC     │              │
//       │               []               └──[ ]───┴─[ ]──┴──────[ ]──┘[]     T      │
//  0    └────────────────┴───────────── couloir ──────────────┤sortie escalier├──────┘
//      0               23              52                           83    90        113   (unités plan, x F1Scale = mètres)
//  T = tableau, flèche = sens du regard des élèves
//
// Les salles ont une porte réseau (SyncedDoor, ouverte par l'admin), des bureaux collés en rangées face au tableau.
// Construit dans la même scène que le rez-de-chaussée, au-dessus : la cage d'escalier de l'étage 1 est placée
// exactement sur l'escalier du rez-de-chaussée (on arrive par le sud dans le couloir). FloorLevel n'affiche que
// l'étage où se trouve le joueur. Appelé après CampusMapBuilder.Build() par Tools > Multijoueur > Créer le menu et la scène de jeu.
public static partial class CampusMapBuilder
{
    // Enveloppe
    const float F1Scale = 0.5f; // mètres par unité plan
    const float F1MaxX = F1Scale * 113f, F1MaxZ = F1Scale * 60f;
    // Aile ouest : S1.3 au sud, S1.4 au nord, zone fermée au nord-ouest
    const float F1WestX1 = F1Scale * 23f, F1S13Z1 = F1Scale * 24f, F1S14Z1 = F1Scale * 44f, F1ClosedX1 = F1Scale * 14.4f;
    // Bloc central : S1.5 / S1.2 / toilettes + ascenseur, entre x = F1CenterX0 et x = F1EastX0 (mur de S1.1)
    const float F1CenterX0 = F1Scale * 52f, F1EastX0 = F1Scale * 90f;
    const float F1ToiletsZ0 = F1Scale * 11.4f, F1S12Z0 = F1Scale * 24.6f, F1S15Z0 = F1Scale * 42.4f;
    const float F1ElevatorX0 = F1Scale * 64.8f, F1ElevatorX1 = F1Scale * 71.4f;
    // Cage d'escalier au sud du couloir, au-dessus de l'escalier du rez-de-chaussée (étage tourné de 90° : la largeur
    // de la cage ici = sa profondeur en bas). On arrive par le palier du haut, à l'extrémité est de la cage, et on
    // tourne pour entrer dans le couloir par l'ouverture du mur sud de la cage.
    const float F1StairsX1 = F1EastX0 - 0.2f, F1StairsWidth = StairsZ1 - StairsZ0, F1StairsDepth = StairsX1 - MinX;
    const float F1StairsX0 = F1StairsX1 - F1StairsWidth;
    // Position de l'étage dans la scène, tourné de 90° : local x -> monde -z (local F1StairsX1 = monde StairsZ0),
    // local z -> monde x (local z = 0, côté couloir = monde StairsX1 ; local z = -profondeur = monde MinX)
    const float F1Yaw = 90f;
    static readonly Vector3 F1Offset = new Vector3(StairsX1, Floor1Y, StairsZ0 + F1StairsX1);

    public static void BuildFloor1()
    {
        Begin("Campus Etage 1", false);

        F1Shell();
        F1Rooms();
        F1Toilets();
        F1Elevator();
        F1Stairs();
        F1Plants();
        F1Ceiling();

        // Construit autour de l'origine, puis tourné et posé au-dessus du rez-de-chaussée (les sièges gardent un point en coordonnées monde)
        Quaternion rotation = Quaternion.Euler(0f, F1Yaw, 0f);
        root.SetPositionAndRotation(F1Offset, rotation);
        foreach (Seat seat in root.GetComponentsInChildren<Seat>(true))
        {
            seat.point = F1Offset + rotation * seat.point;
            seat.yaw += F1Yaw;
        }
        var level = root.gameObject.AddComponent<FloorLevel>();
        level.level = 1;
        level.baseHeight = Floor1Y;

        Finish();
    }

    static void F1Shell()
    {
        Block("Floor", new Vector3(F1MaxX / 2f, -0.04f, F1MaxZ / 2f), new Vector3(F1MaxX, 0.1f, F1MaxZ), hallFloor);

        // Ouverture sur le palier du haut de l'escalier
        float stairsExitX0 = F1StairsX1 - StairsFlatDepth, stairsExitX1 = F1StairsX1 - WallThickness / 2f;
        WallAlongX("Wall South", 0f, 0f, F1MaxX, ((stairsExitX0 + stairsExitX1) / 2f, stairsExitX1 - stairsExitX0, doorHeight));
        WallAlongX("Wall North", F1MaxZ, 0f, F1MaxX);
        WallAlongZ("Wall West", 0f, 0f, F1MaxZ);
        WallAlongZ("Wall East", F1MaxX, 0f, F1MaxZ);

        // Aile ouest
        const float s13DoorZ = F1Scale * 2.5f, s14DoorX = F1Scale * 18.7f;
        WallAlongZ("Wall West Wing", F1WestX1, 0f, F1S14Z1, DoorGap(s13DoorZ));
        WallAlongX("Wall S1.3 N", F1S13Z1, 0f, F1WestX1);
        WallAlongX("Wall S1.4 N", F1S14Z1, 0f, F1WestX1, DoorGap(s14DoorX));
        WallAlongZ("Wall Closed E", F1ClosedX1, F1S14Z1, F1MaxZ);
        Block("Closed Roof", new Vector3(F1ClosedX1 / 2f, WallHeight + 0.05f, (F1S14Z1 + F1MaxZ) / 2f),
            new Vector3(F1ClosedX1 + WallThickness, 0.1f, F1MaxZ - F1S14Z1 + WallThickness), wall);

        // Bloc central
        // Portes au coin de la salle : le tableau occupe le milieu du mur ouest
        const float s12DoorZ = F1S12Z0 + 1f, s15DoorZ = F1S15Z0 + 1f;
        WallAlongZ("Wall Center W", F1CenterX0, F1ToiletsZ0, F1MaxZ, DoorGap(s12DoorZ), DoorGap(s15DoorZ));
        WallAlongX("Wall S1.5 S", F1S15Z0, F1CenterX0, F1EastX0);
        WallAlongX("Wall S1.2 S", F1S12Z0, F1CenterX0, F1EastX0);

        // S1.1, porte côté couloir
        const float s11DoorZ = F1Scale * 6f;
        WallAlongZ("Wall S1.1 W", F1EastX0, 0f, F1MaxZ, DoorGap(s11DoorZ));

        Door(new Vector3(F1WestX1, 0f, s13DoorZ), -90f, "Salle S1.3");
        Door(new Vector3(s14DoorX, 0f, F1S14Z1), 180f, "Salle S1.4");
        Door(new Vector3(F1CenterX0, 0f, s12DoorZ), 90f, "Salle S1.2");
        Door(new Vector3(F1CenterX0, 0f, s15DoorZ), 90f, "Salle S1.5");
        Door(new Vector3(F1EastX0, 0f, s11DoorZ), 90f, "Salle S1.1");
    }

    static void F1Rooms()
    {
        F1Classroom("Salle S1.3", 0f, F1WestX1, 0f, F1S13Z1, 0f);
        F1Classroom("Salle S1.4", 0f, F1WestX1, F1S13Z1, F1S14Z1, 180f);
        F1Classroom("Salle S1.2", F1CenterX0, F1EastX0, F1S12Z0, F1S15Z0, -90f, teacherSide: 1f);
        F1Classroom("Salle S1.5", F1CenterX0, F1EastX0, F1S15Z0, F1MaxZ, -90f, teacherSide: 1f);
        F1Classroom("Salle S1.1", F1EastX0, F1MaxX, 0f, F1MaxZ, 180f);
    }

    // Salle de cours rectangulaire : tableau (et partage de l'organisateur) sur le mur vers lequel regardent les élèves
    // (facingYaw : 0 nord, 90 est, 180 sud, -90 ouest), bureau du prof devant, rangées de bureaux collés à partir du tableau.
    // teacherSide : 0 bureau du prof au milieu, 1 / -1 dans le coin à droite / à gauche du tableau (vu des élèves)
    static void F1Classroom(string label, float x0, float x1, float z0, float z1, float facingYaw,
        int maxRows = 4, int maxPerRow = 8, float teacherSide = 0f)
    {
        var center = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
        Block(label + " Floor", center + Vector3.up * -0.035f, new Vector3(x1 - x0, 0.1f, z1 - z0), roomFloor);

        Quaternion rotation = Quaternion.Euler(0f, facingYaw, 0f);
        Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right;
        bool alongX = Mathf.Abs(forward.x) > 0.5f;
        float halfDepth = (alongX ? x1 - x0 : z1 - z0) / 2f;   // le long du regard
        float halfWidth = (alongX ? z1 - z0 : x1 - x0) / 2f;   // en travers

        float wallOffset = halfDepth - WallThickness / 2f;
        float boardWidth = Mathf.Min(6f, halfWidth * 2f - 2f);
        float boardHeight = Mathf.Min(1.4f, halfWidth * 2f / 4.5f);
        Vector3 boardSize = alongX ? new Vector3(0.05f, 1.5f, boardWidth) : new Vector3(boardWidth, 1.5f, 0.05f);
        Block(label + " Board", center + forward * (wallOffset - 0.03f) + Vector3.up * 1.45f, boardSize, board);
        Sign(label.ToUpperInvariant(), center + forward * (wallOffset - 0.07f) + Vector3.up * 1.45f, 0.22f, new Color(1f, 1f, 1f, 0.85f), facingYaw);
        Display(label + " Board Display", center + forward * (wallOffset - 0.1f) + Vector3.up * 1.45f, facingYaw, boardHeight, true);

        float teacherV = teacherSide * (halfWidth - WallThickness / 2f - deskWidth / 2f - 0.4f);
        Desk(DeskBlack, center + forward * (halfDepth - 2.2f) + right * teacherV, facingYaw + 180f, 2);

        int perRow = Mathf.Clamp(Mathf.FloorToInt((halfWidth * 2f - 2.4f) / deskWidth), 1, maxPerRow);
        for (int row = 0; row < maxRows; row++)
        {
            float u = halfDepth - 4.6f - row * ClassRowSpacing;
            if (u < -halfDepth + 1.4f) break;
            for (int i = 0; i < perRow; i++)
            {
                float v = (i - (perRow - 1) / 2f) * deskWidth;
                Desk(DeskWhite, center + forward * u + right * v, facingYaw, row + i, true);
            }
        }
    }

    // Deux toilettes de part et d'autre de l'ascenseur : entrée sans porte sur le couloir, cabines le long du mur nord,
    // lavabos et miroir le long du mur extérieur
    static void F1Toilets()
    {
        ToiletRoom("WC Ouest", F1CenterX0, F1ElevatorX0, F1ToiletsZ0, F1S12Z0, F1Scale * 58.4f, F1CenterX0, 1f);
        ToiletRoom("WC Est", F1ElevatorX1, F1EastX0, F1ToiletsZ0, F1S12Z0, F1Scale * 86.4f, F1EastX0, -1f);
    }

    // Toilettes entre x0 et x1, z0 (mur d'entrée, sans porte, côté couloir au sud) et z1
    static void ToiletRoom(string name, float x0, float x1, float z0, float z1, float entranceX, float sinkWallX, float sinkSide)
    {
        Block(name + " Floor", new Vector3((x0 + x1) / 2f, -0.035f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.1f, z1 - z0), tile);
        const float entranceWidth = 2f;
        WallAlongX("Wall " + name + " S", z0, x0, x1, (entranceX, entranceWidth, doorHeight));
        Sign("TOILETTES", new Vector3(entranceX, (doorHeight + WallHeight) / 2f, z0 - WallThickness / 2f - 0.02f), 0.18f, MenuStyle.Text * 0.95f);

        // Cabines de 1,6 m le long du mur nord
        const float stallWidth = 1.6f, stallDepth = 2f;
        int stalls = Mathf.FloorToInt((x1 - x0) / stallWidth);
        float stallsX0 = (x0 + x1) / 2f - stalls * stallWidth / 2f;
        for (int i = 0; i < stalls; i++)
        {
            float x = stallsX0 + i * stallWidth;
            if (i > 0)
                Block("Stall Wall", new Vector3(x, 1f, z1 - stallDepth / 2f), new Vector3(0.06f, 2f, stallDepth), wall);
            Block("Toilet", new Vector3(x + stallWidth / 2f, 0.22f, z1 - WallThickness / 2f - 0.3f), new Vector3(0.42f, 0.45f, 0.6f), sanitary);
        }

        // Lavabos + miroir le long du mur latéral, entre l'entrée et les cabines
        float sinksZ0 = z0 + 1f, sinksZ1 = z1 - stallDepth - 1f;
        if (sinksZ1 - sinksZ0 < 1f) return;
        float sinkX = sinkWallX + sinkSide * (WallThickness / 2f + 0.25f);
        Block("Sinks", new Vector3(sinkX, 0.42f, (sinksZ0 + sinksZ1) / 2f), new Vector3(0.5f, 0.85f, sinksZ1 - sinksZ0), sanitary);
        Block("Mirror", new Vector3(sinkWallX + sinkSide * (WallThickness / 2f + 0.02f), 1.55f, (sinksZ0 + sinksZ1) / 2f),
            new Vector3(0.03f, 0.9f, sinksZ1 - sinksZ0 - 0.2f), screen);
    }

    // Cage d'ascenseur fermée entre les deux toilettes, portes métalliques sur le couloir
    static void F1Elevator() => Elevator(F1ElevatorX0, F1ElevatorX1, F1ToiletsZ0, F1S12Z0, 1, 1, "Étage 1");

    // Cage d'ascenseur fermée entre x0 et x1, z0 (portes, côté couloir au sud) et z1
    static void Elevator(float x0, float x1, float z0, float z1, int level, int number, string label)
    {
        WallAlongZ("Wall Elevator W", x0, z0, z1);
        WallAlongZ("Wall Elevator E", x1, z0, z1);
        WallAlongX("Wall Elevator S", z0, x0, x1);
        Block("Elevator Roof", new Vector3((x0 + x1) / 2f, WallHeight + 0.05f, (z0 + z1) / 2f),
            new Vector3(x1 - x0 + WallThickness, 0.1f, z1 - z0 + WallThickness), wall);

        ElevatorDoors(new Vector3((x0 + x1) / 2f, 0f, z0 - WallThickness / 2f), 180f, level, number, label);
    }

    // Cage d'escalier au-dessus de l'escalier du rez-de-chaussée : on arrive sur le palier du haut (côté z > milieu,
    // au bout x > F1StairsX1 - palier) ; le tronçon vers l'étage 4 part du palier d'en face (construit avec l'étage 4)
    static void F1Stairs()
    {
        float z0 = -F1StairsDepth;
        WallAlongZ("Wall Stairs W", F1StairsX0, z0, 0f);
        WallAlongZ("Wall Stairs E", F1StairsX1, z0, 0f);
        WallAlongX("Wall Stairs S", z0, F1StairsX0, F1StairsX1);

        float exitX = F1StairsX1 - (WallThickness / 2f + StairsFlatDepth) / 2f;
        Sign("ESCALIER", new Vector3(exitX, (doorHeight + WallHeight) / 2f, WallThickness / 2f + 0.02f), 0.2f, MenuStyle.Text * 0.95f, 180f);
    }

    static void F1Plants()
    {
        const float m = 0.7f;
        Vector3[] spots =
        {
            new Vector3(F1ClosedX1 + m, 0f, F1MaxZ - m),
            new Vector3(F1WestX1 + m, 0f, F1MaxZ - m),
            new Vector3(F1CenterX0 - m, 0f, F1MaxZ - m),
            new Vector3(F1WestX1 + m, 0f, F1ToiletsZ0 - m),
            new Vector3(F1CenterX0 - m, 0f, F1ToiletsZ0 + m),
            new Vector3(F1StairsX0 + m, 0f, m),
        };
        for (int i = 0; i < spots.Length; i++) TallPlant(spots[i], i);
    }

    // Plafond + dalles lumineuses (visibles en première personne seulement), une grille de dalles par pièce
    static void F1Ceiling()
    {
        Transform group = Group("Ceiling");
        group.gameObject.AddComponent<FirstPersonOnly>();
        const float thickness = 0.05f;
        float y = WallHeight - thickness / 2f;

        // Pas de plafond au-dessus de la cage d'escalier : l'escalier continue vers l'étage 4
        Part("Ceiling", PrimitiveType.Cube, new Vector3(F1MaxX / 2f, y, F1MaxZ / 2f), Quaternion.identity,
            new Vector3(F1MaxX, thickness, F1MaxZ), ceiling, group);

        var rooms = new (float x0, float x1, float z0, float z1)[]
        {
            (0f, F1WestX1, 0f, F1S13Z1),                         // S1.3
            (0f, F1WestX1, F1S13Z1, F1S14Z1),                    // S1.4
            (F1ClosedX1, F1WestX1, F1S14Z1, F1MaxZ),
            (F1WestX1, F1CenterX0, F1ToiletsZ0, F1MaxZ),         // espace ouvert
            (F1WestX1, F1EastX0, 0f, F1ToiletsZ0),               // couloir
            (F1CenterX0, F1EastX0, F1S15Z0, F1MaxZ),             // S1.5
            (F1CenterX0, F1EastX0, F1S12Z0, F1S15Z0),            // S1.2
            (F1CenterX0, F1ElevatorX0, F1ToiletsZ0, F1S12Z0),    // WC ouest
            (F1ElevatorX1, F1EastX0, F1ToiletsZ0, F1S12Z0),      // WC est
            (F1EastX0, F1MaxX, 0f, F1MaxZ),                      // S1.1
        };
        const float spacing = 4.5f, margin = 1.2f;
        foreach (var room in rooms)
        foreach (float x in Spread(room.x0 + margin, room.x1 - margin, spacing))
        foreach (float z in Spread(room.z0 + margin, room.z1 - margin, spacing))
            Part("Ceiling Light", PrimitiveType.Cube, new Vector3(x, y - thickness / 2f - 0.015f, z), Quaternion.identity,
                new Vector3(1.2f, 0.03f, 0.6f), ceilingLight, group);

        foreach (Renderer r in group.GetComponentsInChildren<Renderer>()) r.enabled = false;
    }
}
