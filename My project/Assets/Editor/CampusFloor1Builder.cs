using UnityEngine;

// Carte de l'étage 1 du campus SUPINFO Lille (scène Etage1), d'après le plan plan/Sans titrqqqqe.png.
// Positions du plan relevées à ~0,12 m par pixel du dessin puis multipliées par F1Scale (0,5 : étage deux fois plus petit) ;
// la cage d'escalier garde la taille de l'escalier du rez-de-chaussée. Nord du plan = +Z, 1 unité = 1 m.
//
//  44   ┌──────────┬─────┬───────────────┬───────────────────────────┬──────────────┐
//       │ (fermé)  │     │               │        Salle S1.5        []│              │
//  33   ├──────[]──┘     │               ├───────────────────────────┤              │
//       │ Salle S1.4     │  espace       │        Salle S1.2        []│  Salle S1.1  │
//  18   ├────────────────┤  ouvert       ├────────┬──────┬───────────┤              │
//       │ Salle S1.3     │               │ WC     │ asc. │    WC     │              │
//       │               []               └──[ ]───┴─[ ]──┴──────[ ]──┘[]            │
//  0    └────────────────┴───────────── couloir ──────────────┤sortie escalier├──────┘
//      0               23              52                           83    90        113   (unités plan, x F1Scale = mètres)
//
// Les salles ont une porte réseau (SyncedDoor, ouverte par l'admin), des bureaux collés en rangées face au tableau.
// Construit dans la même scène que le rez-de-chaussée, au-dessus : la cage d'escalier de l'étage 1 est placée
// exactement sur l'escalier du rez-de-chaussée (on arrive par le sud dans le couloir). FloorLevel n'affiche que
// l'étage où se trouve le joueur. Appelé après CampusMapBuilder.Build() par Tools > Multijoueur > Créer le menu et la scène de jeu.
public static partial class CampusMapBuilder
{
    // Enveloppe
    const float F1Scale = 0.5f; // mètres par unité plan
    const float F1MaxX = F1Scale * 113f, F1MaxZ = F1Scale * 44f;
    // Aile ouest : S1.3 au sud, S1.4 au nord, zone fermée au nord-ouest
    const float F1WestX1 = F1Scale * 23f, F1S13Z1 = F1Scale * 18f, F1S14Z1 = F1Scale * 33f, F1ClosedX1 = F1Scale * 14.4f;
    // Bloc central : S1.5 / S1.2 / toilettes + ascenseur, entre x = F1CenterX0 et x = F1EastX0 (mur de S1.1)
    const float F1CenterX0 = F1Scale * 52f, F1EastX0 = F1Scale * 90f;
    const float F1ToiletsZ0 = F1Scale * 11.4f, F1S12Z0 = F1Scale * 24.6f, F1S15Z0 = F1Scale * 38.6f;
    const float F1ElevatorX0 = F1Scale * 64.8f, F1ElevatorX1 = F1Scale * 71.4f;
    // Cage d'escalier au sud du couloir, de la taille de l'escalier du rez-de-chaussée ; on arrive par la moitié est
    const float F1StairsX1 = F1EastX0 - 0.2f, F1StairsWidth = StairsX1 - MinX, F1StairsDepth = StairsZ1 - StairsZ0;
    const float F1StairsX0 = F1StairsX1 - F1StairsWidth, F1StairsOpenX0 = F1StairsX1 - F1StairsWidth / 2f;
    // Position de l'étage dans la scène : sa cage d'escalier au-dessus de celle du rez-de-chaussée
    static readonly Vector3 F1Offset = new Vector3(StairsX1 - F1StairsX1, Floor1Y, StairsZ1);

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

        // Construit autour de l'origine, puis posé au-dessus du rez-de-chaussée (les sièges gardent un point en coordonnées monde)
        root.position = F1Offset;
        foreach (Seat seat in root.GetComponentsInChildren<Seat>(true)) seat.point += F1Offset;
        var level = root.gameObject.AddComponent<FloorLevel>();
        level.level = 1;
        level.baseHeight = Floor1Y;

        Finish();
    }

    static void F1Shell()
    {
        Block("Floor", new Vector3(F1MaxX / 2f, -0.04f, F1MaxZ / 2f), new Vector3(F1MaxX, 0.1f, F1MaxZ), hallFloor);

        WallAlongX("Wall South", 0f, 0f, F1MaxX, ((F1StairsOpenX0 + F1StairsX1) / 2f, F1StairsX1 - F1StairsOpenX0, doorHeight));
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
        const float s12DoorZ = F1Scale * 30.6f, s15DoorZ = F1Scale * 41.3f;
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
        F1Classroom("Salle S1.3", 0f, F1WestX1, 0f, F1S13Z1, -90f);
        F1Classroom("Salle S1.4", 0f, F1WestX1, F1S13Z1, F1S14Z1, -90f);
        F1Classroom("Salle S1.2", F1CenterX0, F1EastX0, F1S12Z0, F1S15Z0, 90f);
        F1Classroom("Salle S1.5", F1CenterX0, F1EastX0, F1S15Z0, F1MaxZ, 90f);
        F1Classroom("Salle S1.1", F1EastX0, F1MaxX, 0f, F1MaxZ, 0f);
    }

    // Salle de cours rectangulaire : tableau (et partage de l'organisateur) sur le mur vers lequel regardent les élèves
    // (facingYaw : 0 nord, 90 est, 180 sud, -90 ouest), bureau du prof devant, rangées de bureaux collés à partir du tableau
    static void F1Classroom(string label, float x0, float x1, float z0, float z1, float facingYaw,
        int maxRows = 4, int maxPerRow = 8)
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

        Desk(DeskBlack, center + forward * (halfDepth - 2.2f), facingYaw + 180f, 2);

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
        F1ToiletRoom("WC Ouest", F1CenterX0, F1ElevatorX0, F1Scale * 58.4f, F1CenterX0, 1f);
        F1ToiletRoom("WC Est", F1ElevatorX1, F1EastX0, F1Scale * 86.4f, F1EastX0, -1f);
    }

    static void F1ToiletRoom(string name, float x0, float x1, float entranceX, float sinkWallX, float sinkSide)
    {
        float z0 = F1ToiletsZ0, z1 = F1S12Z0;
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
    static void F1Elevator()
    {
        WallAlongZ("Wall Elevator W", F1ElevatorX0, F1ToiletsZ0, F1S12Z0);
        WallAlongZ("Wall Elevator E", F1ElevatorX1, F1ToiletsZ0, F1S12Z0);
        WallAlongX("Wall Elevator S", F1ToiletsZ0, F1ElevatorX0, F1ElevatorX1);
        Block("Elevator Roof", new Vector3((F1ElevatorX0 + F1ElevatorX1) / 2f, WallHeight + 0.05f, (F1ToiletsZ0 + F1S12Z0) / 2f),
            new Vector3(F1ElevatorX1 - F1ElevatorX0 + WallThickness, 0.1f, F1S12Z0 - F1ToiletsZ0 + WallThickness), wall);

        float doorX = (F1ElevatorX0 + F1ElevatorX1) / 2f, faceZ = F1ToiletsZ0 - WallThickness / 2f;
        const float leafWidth = 0.8f, leafHeight = 2.1f;
        Block("Elevator Frame", new Vector3(doorX, (leafHeight + 0.1f) / 2f, faceZ - 0.02f), new Vector3(leafWidth * 2f + 0.2f, leafHeight + 0.1f, 0.04f), monitor);
        foreach (float side in new[] { -1f, 1f })
            Block("Elevator Door", new Vector3(doorX + side * (leafWidth / 2f + 0.005f), leafHeight / 2f, faceZ - 0.05f),
                new Vector3(leafWidth - 0.01f, leafHeight, 0.03f), metal);
        Block("Elevator Button", new Vector3(doorX + leafWidth + 0.35f, 1.1f, faceZ - 0.04f), new Vector3(0.09f, 0.16f, 0.04f), accent);
        Sign("ASCENSEUR", new Vector3(doorX, (leafHeight + WallHeight) / 2f + 0.05f, faceZ - 0.03f), 0.16f, MenuStyle.Text * 0.95f);
    }

    // Cage d'escalier : vide au-dessus de l'escalier du rez-de-chaussée (qui monte jusqu'ici par la moitié est),
    // garde-corps le long du vide de la moitié ouest
    static void F1Stairs()
    {
        float z0 = -F1StairsDepth;
        WallAlongZ("Wall Stairs W", F1StairsX0, z0, 0f);
        WallAlongZ("Wall Stairs E", F1StairsX1, z0, 0f);
        WallAlongX("Wall Stairs S", z0, F1StairsX0, F1StairsX1);

        const float railHeight = 1f, landingDepth = 3f;
        float railZ0 = z0 + WallThickness / 2f + landingDepth;
        Block("Stairs Railing", new Vector3(F1StairsOpenX0, railHeight / 2f, railZ0 / 2f), new Vector3(0.06f, railHeight, -railZ0), metal);

        Sign("ESCALIER", new Vector3((F1StairsOpenX0 + F1StairsX1) / 2f, (doorHeight + WallHeight) / 2f, WallThickness / 2f + 0.02f), 0.2f, MenuStyle.Text * 0.95f, 180f);
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
            new Vector3(F1StairsOpenX0 - m, 0f, m),
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

        foreach (var (x0, x1, z0, z1) in new[] { (0f, F1MaxX, 0f, F1MaxZ), (F1StairsX0, F1StairsX1, -F1StairsDepth, 0f) })
            Part("Ceiling", PrimitiveType.Cube, new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f), Quaternion.identity,
                new Vector3(x1 - x0, thickness, z1 - z0), ceiling, group);

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
