using UnityEngine;

// Carte de l'étage 4 du campus SUPINFO Lille, d'après le plan plan/Sans tvgsitre.png (nord du plan = +Z, 1 unité = 1 m).
// Construit directement en coordonnées monde (x, z) au-dessus de l'étage 1, autour de la même cage d'escalier que
// les étages du dessous : on arrive sur le palier du haut et on tourne vers l'est pour entrer.
//
//  74   ┌─────────────────────────────────────────┐ ┐
//       │    grande salle de pause (micro-ondes,   │ │           │
//       │    frigos, tables hautes)                 │           │
//  64   ├──────────────────────┐                     │  balcon   │
//       │   salle de pause      ═ (ouverture) babyfoot  ═           │
//  53   ├───────┬───────┬──────┴──┐                 │           │
//       │  WC   │ asc.  │   WC    │                ═           │
//  47.5 └──[ ]──┴──[]───┴──[ ]────┘   couloir     │           │
//  41.6 ─────────┬──────────────┐                 │           │
//                │  escalier    ═ (sortie)        │           │
//  32            └──────────────┴─────────────────┴───────────┘
//      -14      -4             9.2                26          36
public static partial class CampusMapBuilder
{
    const float F4Y = 2f * Floor1Y;     // juste au-dessus de l'étage 1 (les étages 2 et 3 ne sont pas encore faits)
    const int F4Level = 2;              // numéro d'étage dans la scène (FloorLevel)
    const float F4WestX = -14f, F4EastX = 26f, F4BalconyX = 36f, F4NorthZ = 74f, F4SouthZ = StairsZ0;
    const float F4ToiletsZ0 = 47.5f, F4ToiletsZ1 = 53f, F4ToiletsX1 = 13f;
    const float F4ElevatorX0 = -1.5f, F4ElevatorX1 = 3.5f;
    const float F4PauseX1 = 3f, F4PauseZ1 = 64f;           // salle de pause : x [F4WestX, F4PauseX1], z [F4ToiletsZ1, F4PauseZ1]
    const float F4BigPauseX1 = 16f;                        // grande salle de pause : z [F4PauseZ1, F4NorthZ], ouverte au sud et à l'est
    const float F4BalconyDoorZ1 = 43.4f, F4BalconyDoorZ2 = 54f;

    public static void BuildFloor4()
    {
        Begin("Campus Etage 4", false);

        F4Shell();
        F4Facade();
        F4Balcony();
        F4Stairs();
        ToiletRoom("WC Ouest", F4WestX, F4ElevatorX0, F4ToiletsZ0, F4ToiletsZ1, F4ElevatorX0 - 2f, F4WestX, 1f);
        ToiletRoom("WC Est", F4ElevatorX1, F4ToiletsX1, F4ToiletsZ0, F4ToiletsZ1, F4ElevatorX1 + 2f, F4ToiletsX1, -1f);
        Elevator(F4ElevatorX0, F4ElevatorX1, F4ToiletsZ0, F4ToiletsZ1, F4Level, 4, "Étage 4");
        F4BreakRoom();
        F4BigBreakRoom();
        Babyfoot(new Vector3(11f, 0f, 60.5f));
        F4Plants();
        F4Ceiling();

        // Construit à l'altitude 0, puis monté (les sièges gardent un point en coordonnées monde)
        var offset = new Vector3(0f, F4Y, 0f);
        root.position = offset;
        foreach (Seat seat in root.GetComponentsInChildren<Seat>(true)) seat.point += offset;
        var level = root.gameObject.AddComponent<FloorLevel>();
        level.level = F4Level;
        level.baseHeight = F4Y;

        Finish();
    }

    static void F4Shell()
    {
        // Sol : partie nord sur toute la largeur, partie sud à l'est de la cage d'escalier (pas de sol au-dessus de la cage)
        Block("Floor", new Vector3((F4WestX + F4EastX) / 2f, -0.04f, (StairsZ1 + F4NorthZ) / 2f),
            new Vector3(F4EastX - F4WestX, 0.1f, F4NorthZ - StairsZ1), hallFloor);
        Block("Floor", new Vector3((StairsX1 + F4EastX) / 2f, -0.04f, (F4SouthZ + StairsZ1) / 2f),
            new Vector3(F4EastX - StairsX1, 0.1f, StairsZ1 - F4SouthZ), hallFloor);

        WallAlongZ("Wall West", F4WestX, StairsZ1, F4NorthZ);
        WallAlongX("Wall North", F4NorthZ, F4WestX, F4EastX);
        WallAlongX("Wall South", F4SouthZ, StairsX1, F4EastX);

        // Toilettes / ascenseur : mur nord commun avec la salle de pause, mur est sur la grande pièce
        WallAlongX("Wall Toilets N", F4ToiletsZ1, F4WestX, F4ToiletsX1);
        WallAlongZ("Wall Toilets E", F4ToiletsX1, F4ToiletsZ0, F4ToiletsZ1);

        // Salle de pause : large ouverture à l'est, mur nord le long de la grande salle de pause
        const float pauseOpeningZ = 59f, pauseOpeningWidth = 3f;
        WallAlongZ("Wall Pause E", F4PauseX1, F4ToiletsZ1, F4PauseZ1, Opening(pauseOpeningZ, pauseOpeningWidth));
        WallAlongX("Wall Pause N", F4PauseZ1, F4WestX, F4PauseX1);

    }

    // Façade vitrée sur le balcon (x = F4EastX) : deux portes-fenêtres, des vitres entre elles
    static void F4Facade()
    {
        const float doorWidth = 2f;
        var gaps = new System.Collections.Generic.List<(float, float, float)>
        {
            (F4BalconyDoorZ1, doorWidth, doorHeight),
            (F4BalconyDoorZ2, doorWidth, doorHeight),
        };
        var windows = new System.Collections.Generic.List<float>();
        float spacing = WindowWidth + 0.8f;
        foreach (var (a, b) in new[] { (F4SouthZ + 0.8f, F4BalconyDoorZ1 - 1.6f), (F4BalconyDoorZ1 + 1.6f, F4BalconyDoorZ2 - 1.6f), (F4BalconyDoorZ2 + 1.6f, F4NorthZ - 0.8f) })
            if (b - a >= WindowWidth)
                windows.AddRange(Spread(a + WindowWidth / 2f, b - WindowWidth / 2f, spacing));
        foreach (float z in windows) gaps.Add((z, WindowWidth, WindowTop));
        WallAlongZ("Wall Facade", F4EastX, F4SouthZ, F4NorthZ, gaps.ToArray());

        foreach (float z in windows)
        {
            Block("Window Sill", new Vector3(F4EastX, WindowSill / 2f, z), new Vector3(WallThickness, WindowSill, WindowWidth), wall);
            Block("Window Frame", new Vector3(F4EastX, WindowSill + 0.02f, z), new Vector3(WallThickness + 0.06f, 0.04f, WindowWidth), monitor);
            Block("Window Glass", new Vector3(F4EastX, (WindowSill + WindowTop) / 2f, z), new Vector3(0.03f, WindowTop - WindowSill, WindowWidth), glass);
        }
        foreach (float z in new[] { F4BalconyDoorZ1, F4BalconyDoorZ2 })
            Sign("BALCON", new Vector3(F4EastX - WallThickness / 2f - 0.02f, (doorHeight + WallHeight) / 2f, z), 0.16f, MenuStyle.Text * 0.95f, 90f);
    }

    // Balcon : dalle extérieure, garde-corps vitré sur les trois côtés ouverts, bancs et plantes
    static void F4Balcony()
    {
        Block("Balcony Floor", new Vector3((F4EastX + F4BalconyX) / 2f, -0.04f, (F4SouthZ + F4NorthZ) / 2f),
            new Vector3(F4BalconyX - F4EastX, 0.1f, F4NorthZ - F4SouthZ), sidewalk);

        const float railHeight = 1.05f;
        void Railing(Vector3 a, Vector3 b)
        {
            Vector3 mid = (a + b) / 2f;
            bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
            float length = alongX ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.z - a.z);
            Vector3 Size(float thickness, float height) => alongX ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length);
            Block("Balcony Glass", mid + Vector3.up * railHeight / 2f, Size(0.03f, railHeight), glass, "Balcony");
            Block("Balcony Rail", mid + Vector3.up * railHeight, Size(0.08f, 0.05f), metal, "Balcony");
        }
        Railing(new Vector3(F4BalconyX, 0f, F4SouthZ), new Vector3(F4BalconyX, 0f, F4NorthZ));
        Railing(new Vector3(F4EastX, 0f, F4SouthZ), new Vector3(F4BalconyX, 0f, F4SouthZ));
        Railing(new Vector3(F4EastX, 0f, F4NorthZ), new Vector3(F4BalconyX, 0f, F4NorthZ));

        // Bancs le long du garde-corps
        foreach (float z in new[] { 40f, 50f, 60f, 68f })
        {
            Block("Bench", new Vector3(F4BalconyX - 0.6f, 0.42f, z), new Vector3(0.5f, 0.06f, 2.2f), wood, "Balcony");
            foreach (float dz in new[] { -0.9f, 0.9f })
                Block("Bench Leg", new Vector3(F4BalconyX - 0.6f, 0.2f, z + dz), new Vector3(0.4f, 0.4f, 0.08f), metal, "Balcony");
        }
    }

    // Palier d'arrivée de l'escalier (tronçon depuis l'étage 1), cage fermée, sortie vers l'est sur le palier du haut
    static void F4Stairs()
    {
        StairsRun(-Floor1Y, false, 1, F4Level);

        float exitZ0 = StairsZ0 + WallThickness / 2f, exitZ1 = StairsFlightZ0;
        WallAlongZ("Wall Stairs W", MinX, StairsZ0, StairsZ1);
        WallAlongX("Wall Stairs S", StairsZ0, MinX, StairsX1);
        WallAlongX("Wall Stairs N", StairsZ1, F4WestX, StairsX1); // et mur sud du bâtiment à l'ouest de la cage
        WallAlongZ("Wall Stairs E", StairsX1, StairsZ0, StairsZ1, ((exitZ0 + exitZ1) / 2f, exitZ1 - exitZ0, doorHeight));
        Sign("ESCALIER", new Vector3(StairsX1 + WallThickness / 2f + 0.02f, (doorHeight + WallHeight) / 2f, (exitZ0 + exitZ1) / 2f),
            0.2f, MenuStyle.Text * 0.95f, -90f);

        // Dernier étage : garde-corps entre le palier d'arrivée (est) et le vide au-dessus de la volée qui monte (ouest)
        const float railHeight = 1f;
        float railZ0 = StairsZ0 + WallThickness / 2f;
        Block("Stairs Railing", new Vector3(StairsMidX, railHeight / 2f, (railZ0 + StairsMidLandingZ0) / 2f),
            new Vector3(0.06f, railHeight, StairsMidLandingZ0 - railZ0), metal, "Stairs");
    }

    // Salle de pause : tables rondes, tables collées le long du mur ouest, canapé le long du mur nord
    static void F4BreakRoom()
    {
        float centerX = (F4WestX + F4PauseX1) / 2f, centerZ = (F4ToiletsZ1 + F4PauseZ1) / 2f;
        Sign("SALLE DE PAUSE", new Vector3(centerX, 2f, F4PauseZ1 - WallThickness / 2f - 0.02f), 0.24f, MenuStyle.Text * 0.95f);
        RoundTable(new Vector3(centerX - 2.5f, 0f, centerZ), 1.6f);
        RoundTable(new Vector3(centerX + 3f, 0f, centerZ - 1f), 1.6f);

        const float tableLength = 1.8f, tableDepth = 0.9f;
        float wallX = F4WestX + WallThickness / 2f + tableDepth / 2f;
        foreach (float z in Glued(F4ToiletsZ1 + 1f, F4PauseZ1 - 1f, tableLength))
            RectTable(new Vector3(wallX, 0f, z), tableDepth, tableLength);

        // Canapé contre le mur nord : assise + dossier
        float sofaZ = F4PauseZ1 - WallThickness / 2f - 0.45f;
        const float sofaLength = 4.4f;
        Block("Sofa", new Vector3(centerX, 0.22f, sofaZ), new Vector3(sofaLength, 0.44f, 0.9f), fabric, "Break Room");
        Block("Sofa Back", new Vector3(centerX, 0.6f, sofaZ + 0.35f), new Vector3(sofaLength, 0.6f, 0.2f), fabric, "Break Room");
    }

    // Grande salle de pause : frigos et 4 tables à micro-ondes contre le mur nord, tables hautes rectangulaires avec tabourets
    static void F4BigBreakRoom()
    {
        float wallZ = F4NorthZ - WallThickness / 2f;
        Sign("GRANDE SALLE DE PAUSE", new Vector3((F4WestX + F4BigPauseX1) / 2f, 2.3f, wallZ - 0.02f), 0.24f, MenuStyle.Text * 0.95f);

        const float fridgeWidth = 0.85f, tableLength = 1.8f, tableDepth = 0.9f;
        foreach (float x in Glued(F4WestX + 0.5f, F4WestX + 0.5f + 4f * fridgeWidth, fridgeWidth))
            Fridge(new Vector3(x, 0f, wallZ), fridgeWidth);
        float tablesZ = wallZ - tableDepth / 2f;
        const int microwaves = 4;
        float tablesX0 = F4WestX + 1f + 4f * fridgeWidth;
        foreach (float x in Glued(tablesX0, tablesX0 + microwaves * tableLength, tableLength))
        {
            RectTable(new Vector3(x, 0f, tablesZ), tableLength, tableDepth);
            Microwave(new Vector3(x, 0.75f, tablesZ + 0.1f));
        }

        // Tables hautes (mange-debout) en deux rangées
        foreach (float z in new[] { 67f, 70f })
        foreach (float x in Spread(F4WestX + 2.5f, F4EastX - 3f, 4f))
            HighTable(new Vector3(x, 0f, z));
    }

    // Table haute rectangulaire (1,05 m, long côté le long de X) et deux tabourets hauts de chaque long côté
    static void HighTable(Vector3 position)
    {
        const float height = 1.05f, top = 0.05f, length = 1.8f, depth = 0.8f, leg = 0.06f;
        Block("High Table", position + Vector3.up * (height - top / 2f), new Vector3(length, top, depth), wood, "Break Room");
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Block("High Table Leg", position + new Vector3(sx * (length / 2f - leg), (height - top) / 2f, sz * (depth / 2f - leg)),
                new Vector3(leg, height - top, leg), monitor, "Break Room");
        foreach (float sx in new[] { -0.45f, 0.45f })
        foreach (float sz in new[] { -1f, 1f })
        {
            Vector3 p = position + new Vector3(sx, 0f, sz * (depth / 2f + 0.35f));
            Part("Stool Seat", PrimitiveType.Cylinder, p + Vector3.up * 0.74f, Quaternion.identity, new Vector3(0.38f, 0.03f, 0.38f), fabric, Group("Break Room"));
            Block("Stool Leg", p + Vector3.up * 0.36f, new Vector3(0.05f, 0.72f, 0.05f), metal, "Break Room");
        }
    }

    // Babyfoot (long côté le long de Z) : caisse, terrain, barres avec poignées et joueurs
    static void Babyfoot(Vector3 position)
    {
        const string g = "Break Room";
        const float length = 1.4f, width = 0.75f, top = 0.92f, boxHeight = 0.22f;
        Block("Babyfoot", position + Vector3.up * (top - boxHeight / 2f), new Vector3(width, boxHeight, length), wood, g);
        Block("Babyfoot Field", position + Vector3.up * (top - boxHeight + 0.07f), new Vector3(width - 0.06f, 0.01f, length - 0.06f), leaves, g);
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Block("Babyfoot Leg", position + new Vector3(sx * (width / 2f - 0.06f), (top - boxHeight) / 2f, sz * (length / 2f - 0.06f)),
                new Vector3(0.08f, top - boxHeight, 0.08f), monitor, g);

        Transform group = Group(g);
        for (int i = 0; i < 8; i++)
        {
            float z = position.z - length / 2f + length * (i + 0.5f) / 8f;
            float y = position.y + top - 0.06f;
            Part("Babyfoot Rod", PrimitiveType.Cylinder, new Vector3(position.x, y, z), Quaternion.Euler(0f, 0f, 90f),
                new Vector3(0.016f, width / 2f + 0.25f, 0.016f), metal, group);
            float handleX = position.x + (i % 2 == 0 ? -1f : 1f) * (width / 2f + 0.25f);
            Part("Babyfoot Handle", PrimitiveType.Cylinder, new Vector3(handleX, y, z), Quaternion.Euler(0f, 0f, 90f),
                new Vector3(0.04f, 0.06f, 0.04f), monitor, group);
            Material team = i % 2 == 0 ? accent : doorBlue;
            int players = i == 0 || i == 7 ? 1 : (i == 3 || i == 4 ? 5 : (i == 1 || i == 6 ? 2 : 3));
            const float span = width - 0.12f;
            for (int k = 0; k < players; k++)
            {
                float px = position.x - span / 2f + span * (k + 0.5f) / players;
                Part("Babyfoot Player", PrimitiveType.Cube, new Vector3(px, y - 0.03f, z), Quaternion.identity,
                    new Vector3(0.04f, 0.1f, 0.03f), team, group);
            }
        }
    }

    static void F4Plants()
    {
        const float m = 0.7f;
        Vector3[] spots =
        {
            new Vector3(StairsX1 + m, 0f, F4SouthZ + StairsFlatDepth + m),   // en face de la sortie de l'escalier
            new Vector3(F4EastX - m, 0f, F4SouthZ + m),
            new Vector3(F4ToiletsX1 + m, 0f, F4ToiletsZ0 - m),
            new Vector3(F4PauseX1 + m, 0f, F4PauseZ1 - m),
            new Vector3(F4EastX - m, 0f, F4NorthZ - m),
            new Vector3(F4EastX + m, 0f, F4SouthZ + m),                     // balcon
            new Vector3(F4BalconyX - m, 0f, F4NorthZ - m),
            new Vector3(F4EastX + m, 0f, (F4BalconyDoorZ1 + F4BalconyDoorZ2) / 2f),
        };
        for (int i = 0; i < spots.Length; i++) TallPlant(spots[i], i);
    }

    // Plafond + dalles lumineuses (visibles en première personne seulement), pas au-dessus du balcon
    static void F4Ceiling()
    {
        Transform group = Group("Ceiling");
        group.gameObject.AddComponent<FirstPersonOnly>();
        const float thickness = 0.05f;
        float y = WallHeight - thickness / 2f;

        foreach (var (x0, x1, z0, z1) in new[] { (F4WestX, F4EastX, StairsZ1, F4NorthZ), (MinX, F4EastX, F4SouthZ, StairsZ1) })
            Part("Ceiling", PrimitiveType.Cube, new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f), Quaternion.identity,
                new Vector3(x1 - x0, thickness, z1 - z0), ceiling, group);

        var rooms = new (float x0, float x1, float z0, float z1)[]
        {
            (F4WestX, StairsX1, StairsZ1, F4ToiletsZ0),             // couloir au-dessus de l'escalier
            (StairsX1, F4EastX, F4SouthZ, F4ToiletsZ0),             // sortie de l'escalier
            (F4ToiletsX1, F4EastX, F4ToiletsZ0, F4PauseZ1),         // espace babyfoot
            (F4PauseX1, F4ToiletsX1, F4ToiletsZ1, F4PauseZ1),
            (F4WestX, F4PauseX1, F4ToiletsZ1, F4PauseZ1),           // salle de pause
            (F4WestX, F4EastX, F4PauseZ1, F4NorthZ),                // grande salle de pause
            (F4WestX, F4ElevatorX0, F4ToiletsZ0, F4ToiletsZ1),      // WC
            (F4ElevatorX1, F4ToiletsX1, F4ToiletsZ0, F4ToiletsZ1),
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
