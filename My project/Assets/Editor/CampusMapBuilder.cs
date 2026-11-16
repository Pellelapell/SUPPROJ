using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Carte de la scène de jeu : rez-de-chaussée (étage 0) du campus SUPINFO Lille, d'après le plan plan/Sans titre.png.
// Le nord du plan est +Z, la rue Colson est au sud (z < 0), 1 unité = 1 m.
// Les positions du plan sont relevées en "unités plan" (~0,06 m par pixel du dessin) puis multipliées par Scale :
// changer Scale agrandit ou rétrécit tout le bâtiment (les meubles, les portes et la hauteur des murs ne changent pas,
// les salles se remplissent avec plus ou moins de bureaux).
//
//  plan 30 ┌──────────┬────────┬──────────────┬──────────┬────────┐  (salles du fond : RoomDepth m de profondeur)
//          │ Stockage │ tables │   Salle S.01 │  Bureau  │  S.03  │
//  plan 25 ├──────────┘        └──────[]──────┴───[]─────┴──[]────┤
//          │ Escalier │      Salle de pause            │          │
//  plan~20 │          │  ┌─────────┐ ┌────[]─────────────────────┤
//          │          │  │Toilettes│ │                           │
//  plan 13 │ Comptoir │  └─────────┘ []        Amphithéâtre      │
//          │      Hall d'entrée      │                   estrade │
//  plan 7  └──┤entrée├───────────────┴───────────────────────────┘
//                           RUE COLSON (zone d'apparition)
//       plan x -2                  plan x 16.3                  plan x 34
//
// Les portes WoodDoors (S.01, Bureau, S.03 et les deux de l'amphi) sont pilotées par le réseau (SyncedDoor) :
// seul l'admin peut les ouvrir. Chaque chaise de bureau est un siège (Seat). Les bureaux des élèves ont un écran de PC,
// et les tableaux / le grand écran de l'amphi un écran (ScreenDisplay) : tous affichent le partage de l'organisateur.
// Appelé par Tools > Multijoueur > Créer le menu et la scène de jeu.
public static class CampusMapBuilder
{
    const string MaterialsDir = "Assets/Materials/Map";
    const string ThirdParty = "Assets/ThirdParty/";
    const string DoorPrefab = ThirdParty + "WoodDoors/Prefab/Wood/Door_1/Door_1_Venge.prefab";
    const string DeskWhite = ThirdParty + "DeskTable/Desk Table White.prefab";
    const string DeskBlack = ThirdParty + "DeskTable/Desk Table Black.prefab";
    const string DeskBlue = ThirdParty + "DeskTable/Desk Table Blue.prefab";
    static readonly string[] Chairs =
    {
        ThirdParty + "OfficeChairs/Prefabs/Chair1.prefab",
        ThirdParty + "OfficeChairs/Prefabs/Chair2.prefab",
        ThirdParty + "OfficeChairs/Prefabs/Chair3.prefab",
    };
    const string Props = ThirdParty + "CityPeople/Prefabs/PROPS/dummy props/";

    // Orientation des assets : à ajuster ici si un bureau ou une chaise apparaît à l'envers
    const float DeskYaw = 0f;     // bureau "face" vers +Z
    const float ChairYaw = 0f;    // chaise tournée vers son bureau
    const float ChairGap = 0.35f; // espace entre le bord du bureau et la chaise

    const float WallHeight = 2.6f;
    const float WallThickness = 0.2f;

    // ---------- Plan de l'étage 0 ----------
    const float Scale = 2f; // mètres par unité plan

    // Enveloppe du bâtiment
    const float MinX = Scale * -2f, MaxX = Scale * 34f;
    const float FrontZ = Scale * 7f;                            // façade rue Colson
    const float EntranceX = 0f, EntranceWidth = Scale * 3f;
    // Rangée du fond : Stockage (fermé) | grande table | S.01 | Bureau | S.03
    const float BackRowZ = Scale * 25f;
    const float RoomDepth = 10f;                                // profondeur des salles du fond (mètres)
    const float BackZ = BackRowZ + RoomDepth;                   // mur du fond
    const float StorageX1 = Scale * 5f;
    const float S01X0 = Scale * 10.3f, S01X1 = Scale * 20.5f;
    const float OfficeX1 = Scale * 27.9f;
    // Escalier (vers l'étage 1) et comptoir d'accueil, contre le mur ouest
    const float StairsX1 = Scale * 4.6f, StairsZ0 = Scale * 16f, StairsZ1 = Scale * 20.8f;
    // Bloc toilettes et amphithéâtre (le mur x = AmphiX0 est partagé)
    const float ToiletsX0 = Scale * 9.4f, ToiletsZ0 = Scale * 13f;
    const float AmphiX0 = Scale * 16.3f, AmphiZ1 = Scale * 19.6f; // haut des toilettes et de l'amphi
    const float StageDepth = Scale * 4f;
    // Rue
    const float SidewalkZ0 = Scale * -1f, RoadZ0 = Scale * -9f;

    // Positions des portes (centre le long du mur)
    const float S01DoorX = Scale * 15.7f, OfficeDoorX = Scale * 23f, S03DoorX = Scale * 30.5f;
    const float AmphiSideDoorZ = Scale * 11.5f, AmphiTopDoorX = Scale * 17.8f;

    // Espacement du mobilier (ne dépend pas de Scale)
    const float ClassRowSpacing = 4.4f; // entre deux rangées de bureaux collés
    const float AmphiRowSpacing = 2.6f, AmphiSeatSpacing = 2.1f, AmphiAisle = 2.4f;

    static Transform root;
    static Material wall, hallFloor, roomFloor, board, screen, stage, accent, monitor, display, tile, sanitary, road, sidewalk, marking, ceiling, ceilingLight, wood, fabric, doorBlue, metal, appliance, signText;
    static float doorWidth, doorHeight, deskWidth, deskDepth;
    static int doorCount, seatCount;

    public static void Build()
    {
        root = new GameObject("Campus").transform;
        doorCount = 0;
        seatCount = 0;
        CreateMaterials();
        MeasureDoor();
        MeasureDesk();

        BuildShell();
        BuildHall();
        BuildStairs();
        BuildToilets();
        BuildAmphitheater();
        BuildBreakRoom();
        BuildStorage();
        BuildClassroom("Salle S.01", S01X0, S01X1);
        BuildOffice();
        BuildClassroom("Salle S.03", OfficeX1, MaxX);
        BuildStreet();
        BuildCeiling();

        // La texture de la police peut changer en jeu : le matériau des écritures la suit
        var signs = Group("Signs").gameObject.AddComponent<SignText>();
        signs.material = signText;
        signs.font = MenuStyle.Font;

        // Aucune ombre dans le bâtiment : ni portée par les murs et le mobilier, ni reçue (intérieur uniformément éclairé) ;
        // les immeubles de la rue gardent les leurs
        Transform street = Group("Street");
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform.IsChildOf(street)) continue;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Lumière ambiante forte et venant de partout : les faces des murs à contre-jour restent claires (pas de murs noirs)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.82f, 0.84f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.74f, 0.75f, 0.78f);
        RenderSettings.ambientGroundColor = new Color(0.6f, 0.6f, 0.62f);
    }

    // ---------- Murs, sols, portes ----------

    static (float, float, float) DoorGap(float center) => (center, doorWidth, doorHeight);
    static (float, float, float) Opening(float center, float width) => (center, width, WallHeight);

    static void BuildShell()
    {
        Block("Floor", new Vector3((MinX + MaxX) / 2f, -0.04f, (FrontZ + BackZ) / 2f), new Vector3(MaxX - MinX, 0.1f, BackZ - FrontZ), hallFloor);

        // Enveloppe : façade avec l'entrée (sans porte) côté rue Colson
        WallAlongX("Wall Front", FrontZ, MinX, MaxX, Opening(EntranceX, EntranceWidth));
        WallAlongX("Wall Back", BackZ, MinX, MaxX);
        WallAlongZ("Wall West", MinX, FrontZ, BackZ);
        WallAlongZ("Wall East", MaxX, FrontZ, BackZ);

        // Rangée du fond : stockage (fermé), puis (après le coin des distributeurs) S.01, bureau, S.03
        WallAlongX("Wall Storage", BackRowZ, MinX, StorageX1); // stockage fermé (BuildStorage)
        WallAlongZ("Wall Storage E", StorageX1, BackRowZ, BackZ);
        WallAlongX("Wall Rooms", BackRowZ, S01X0, MaxX, DoorGap(S01DoorX), DoorGap(OfficeDoorX), DoorGap(S03DoorX));
        WallAlongZ("Wall S.01 W", S01X0, BackRowZ, BackZ);
        WallAlongZ("Wall S.01 E", S01X1, BackRowZ, BackZ);
        WallAlongZ("Wall S.03 W", OfficeX1, BackRowZ, BackZ);

        // Amphithéâtre : porte côté hall (mur ouest) et porte côté salle de pause (mur nord)
        WallAlongZ("Wall Amphi W", AmphiX0, FrontZ, AmphiZ1, DoorGap(AmphiSideDoorZ));
        WallAlongX("Wall Amphi N", AmphiZ1, AmphiX0, MaxX, DoorGap(AmphiTopDoorX));

        Door(new Vector3(S01DoorX, 0f, BackRowZ), 0f, "Salle S.01");
        Door(new Vector3(OfficeDoorX, 0f, BackRowZ), 0f, "Bureau");
        Door(new Vector3(S03DoorX, 0f, BackRowZ), 0f, "Salle S.03");
        Door(new Vector3(AmphiX0, 0f, AmphiSideDoorZ), 90f, "Amphithéâtre");
        Door(new Vector3(AmphiTopDoorX, 0f, AmphiZ1), 180f, "Amphithéâtre"); // on entre vers le sud

        // Enseigne au-dessus de l'entrée
        Block("Facade Sign", new Vector3(EntranceX, WallHeight + 0.55f, FrontZ), new Vector3(EntranceWidth + 2f, 1.1f, 0.25f), accent);
        Sign("SUPINFO", new Vector3(EntranceX, WallHeight + 0.55f, FrontZ - 0.15f), 0.75f, Color.white);
    }

    // Mur le long de X (à z fixe), avec des ouvertures (centre x, largeur, hauteur). Au-dessus d'une ouverture
    // moins haute que le mur, un linteau complète le mur.
    static void WallAlongX(string name, float z, float x0, float x1, params (float center, float width, float height)[] gaps)
    {
        float x = x0;
        foreach (var gap in gaps.OrderBy(g => g.center))
        {
            float left = gap.center - gap.width / 2f, right = gap.center + gap.width / 2f;
            WallSegment(name, new Vector3((x + left) / 2f, 0f, z), new Vector3(left - x, WallHeight, WallThickness));
            if (gap.height < WallHeight)
                Block(name + " Lintel", new Vector3(gap.center, (gap.height + WallHeight) / 2f, z),
                    new Vector3(gap.width, WallHeight - gap.height, WallThickness), wall);
            x = right;
        }
        WallSegment(name, new Vector3((x + x1) / 2f, 0f, z), new Vector3(x1 - x, WallHeight, WallThickness));
    }

    // Mur le long de Z (à x fixe), mêmes ouvertures que WallAlongX (centre z, largeur, hauteur)
    static void WallAlongZ(string name, float x, float z0, float z1, params (float center, float width, float height)[] gaps)
    {
        float z = z0;
        foreach (var gap in gaps.OrderBy(g => g.center))
        {
            float low = gap.center - gap.width / 2f, high = gap.center + gap.width / 2f;
            WallSegment(name, new Vector3(x, 0f, (z + low) / 2f), new Vector3(WallThickness, WallHeight, low - z));
            if (gap.height < WallHeight)
                Block(name + " Lintel", new Vector3(x, (gap.height + WallHeight) / 2f, gap.center),
                    new Vector3(WallThickness, WallHeight - gap.height, gap.width), wall);
            z = high;
        }
        WallSegment(name, new Vector3(x, 0f, (z + z1) / 2f), new Vector3(WallThickness, WallHeight, z1 - z));
    }

    static void WallSegment(string name, Vector3 bottomCenter, Vector3 size)
    {
        if (size.x <= 0.01f || size.z <= 0.01f) return;
        Block(name, bottomCenter + Vector3.up * size.y / 2f, size, wall);
    }

    // Taille du cadre de porte, pour découper les murs à la bonne largeur
    static void MeasureDoor()
    {
        var door = (GameObject)PrefabUtility.InstantiatePrefab(Load(DoorPrefab));
        Bounds b = WorldBounds(door);
        doorWidth = b.size.x;
        doorHeight = b.size.y;
        Object.DestroyImmediate(door);
    }

    // Largeur et profondeur d'un bureau (pour coller les bureaux côte à côte ou face à face)
    static void MeasureDesk()
    {
        var desk = (GameObject)PrefabUtility.InstantiatePrefab(Load(DeskWhite));
        desk.transform.rotation = Quaternion.Euler(0f, DeskYaw, 0f);
        Bounds bounds = WorldBounds(desk);
        deskWidth = Mathf.Max(0.6f, bounds.size.x);
        deskDepth = Mathf.Max(0.4f, bounds.size.z);
        Object.DestroyImmediate(desk);
    }

    // Porte réseau. yaw = direction dans laquelle on entre (0 : vers +Z, 90 : vers +X, 180 : vers -Z) ; le nom est affiché côté extérieur.
    static void Door(Vector3 position, float yaw, string label)
    {
        GameObject door = Place(DoorPrefab, position, yaw, "Doors");
        if (door == null) return;
        // Porte et cadre en bleu (la poignée garde son métal)
        foreach (Renderer r in door.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => m != null && m.name.StartsWith("Door_") ? doorBlue : m).ToArray();
        var synced = door.AddComponent<SyncedDoor>();
        synced.index = doorCount++;
        synced.label = label;

        // Son de porte en 3D : entendu seulement à proximité
        foreach (var source in door.GetComponentsInChildren<AudioSource>())
        {
            source.spatialBlend = 1f;
            source.maxDistance = 18f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.playOnAwake = false;
        }

        // Nom de la salle au-dessus de la porte, côté couloir
        Vector3 outside = Quaternion.Euler(0f, yaw, 0f) * Vector3.back * (WallThickness / 2f + 0.02f);
        Sign(label.ToUpperInvariant(), position + outside + Vector3.up * (doorHeight + WallHeight) / 2f, 0.16f, MenuStyle.Text * 0.95f, yaw);
    }

    // Objets de taille size collés les uns aux autres, autant que possible dans [from, to], centrés
    static float[] Glued(float from, float to, float size)
    {
        int count = Mathf.Max(1, Mathf.FloorToInt((to - from) / size));
        float start = (from + to) / 2f - (count - 1) * size / 2f;
        return Enumerable.Range(0, count).Select(i => start + i * size).ToArray();
    }

    // Positions régulièrement espacées dans [from, to] (au moins une, centrée si la place manque)
    static float[] Spread(float from, float to, float spacing)
    {
        int count = Mathf.Max(1, Mathf.FloorToInt((to - from) / spacing) + 1);
        float start = (from + to) / 2f - (count - 1) * spacing / 2f;
        return Enumerable.Range(0, count).Select(i => start + i * spacing).ToArray();
    }

    // ---------- Pièces ----------

    // Hall d'entrée : comptoir d'accueil à gauche de l'entrée, étagère derrière
    static void BuildHall()
    {
        const float counterX0 = MinX + 0.2f, counterX1 = Scale * 3.6f, counterZ = Scale * 11.4f, shelfZ = Scale * 13.7f;
        float counterCenter = (counterX0 + counterX1) / 2f;
        Block("Counter", new Vector3(counterCenter, 0.55f, counterZ), new Vector3(counterX1 - counterX0, 1.1f, 0.8f), wood);
        Block("Counter Top", new Vector3(counterCenter, 1.12f, counterZ), new Vector3(counterX1 - counterX0 + 0.1f, 0.05f, 0.9f), accent);
        // Mur derrière le comptoir, sur toute la hauteur, depuis le mur ouest
        WallAlongX("Wall Counter", shelfZ, MinX, counterX1 + 0.6f);


        // Banc pouf en U dans le hall, à droite (près du mur de l'amphi) : les deux bras collés au mur de façade,
        // le fond du U côté intérieur (ouvert vers la façade)
        const float poufHeight = 0.45f, poufDepth = 0.8f, poufLength = 6f, poufArm = 2.4f;
        float poufX1 = AmphiX0 - WallThickness / 2f - 1.5f, poufX0 = poufX1 - poufLength;
        float poufZ0 = FrontZ + WallThickness / 2f;
        foreach (float x in new[] { poufX0 + poufDepth / 2f, poufX1 - poufDepth / 2f })
            Block("Pouf", new Vector3(x, poufHeight / 2f, poufZ0 + poufArm / 2f), new Vector3(poufDepth, poufHeight, poufArm), fabric, "Hall");
        Block("Pouf", new Vector3((poufX0 + poufX1) / 2f, poufHeight / 2f, poufZ0 + poufArm + poufDepth / 2f), new Vector3(poufLength, poufHeight, poufDepth), fabric, "Hall");

    }

    // Escalier vers l'étage 1 en deux volées côte à côte avec un demi-tour (180°) sur un palier au fond :
    // on monte vers le nord par la moitié gauche, on fait demi-tour sur le palier, puis on monte vers le sud par la moitié droite.
    // Fermé par une barrière en bas tant que l'étage 1 n'existe pas.
    static void BuildStairs()
    {
        WallAlongZ("Wall Stairs E", StairsX1, StairsZ0, StairsZ1);
        WallAlongX("Wall Stairs N", StairsZ1, MinX, StairsX1);

        const int stepsPerFlight = 8;
        const float landingDepth = 3f;
        float rise = WallHeight / (2 * stepsPerFlight + 1); // palier = une marche entre les deux volées
        float x0 = MinX + WallThickness / 2f, x1 = StairsX1 - WallThickness / 2f, xMid = (x0 + x1) / 2f;
        float landingZ0 = StairsZ1 - WallThickness / 2f - landingDepth;
        float run = (landingZ0 - StairsZ0) / stepsPerFlight;
        float leftX0 = x0, leftX1 = xMid - WallThickness / 2f, rightX0 = xMid + WallThickness / 2f, rightX1 = x1;

        for (int i = 0; i < stepsPerFlight; i++)
        {
            float topUp = rise * (i + 1);                       // volée 1 : vers le nord
            Block("Step A" + (i + 1), new Vector3((leftX0 + leftX1) / 2f, topUp / 2f, StairsZ0 + run * (i + 0.5f)),
                new Vector3(leftX1 - leftX0, topUp, run), wood);
            float topBack = rise * (stepsPerFlight + 2 + i);    // volée 2 : vers le sud, après le palier
            Block("Step B" + (i + 1), new Vector3((rightX0 + rightX1) / 2f, topBack / 2f, landingZ0 - run * (i + 0.5f)),
                new Vector3(rightX1 - rightX0, topBack, run), wood);
        }
        float landingTop = rise * (stepsPerFlight + 1);
        Block("Landing", new Vector3((x0 + x1) / 2f, landingTop / 2f, (landingZ0 + StairsZ1 - WallThickness / 2f) / 2f),
            new Vector3(x1 - x0, landingTop, landingDepth), wood);

        // Mur entre les deux volées, jusqu'au palier
        WallAlongZ("Wall Stairs Mid", xMid, StairsZ0, landingZ0);

        float barrierX = (leftX0 + leftX1) / 2f;
        Block("Stairs Barrier", new Vector3(barrierX, 0.5f, StairsZ0 - 0.3f), new Vector3(leftX1 - leftX0, 1f, 0.08f), accent);
        Sign("ÉTAGE 1", new Vector3(barrierX, 0.6f, StairsZ0 - 0.36f), 0.2f, Color.white);
    }

    // Toilettes : entrée sans porte côté hall (mur ouest), surmontée d'un linteau avec le nom, lavabos au nord, cabines le long du mur côté amphi
    static void BuildToilets()
    {
        float x1 = AmphiX0, z1 = AmphiZ1;
        Block("Toilets Floor", new Vector3((ToiletsX0 + x1) / 2f, -0.035f, (ToiletsZ0 + z1) / 2f), new Vector3(x1 - ToiletsX0, 0.1f, z1 - ToiletsZ0), tile);
        const float entranceZ = Scale * 14.3f, entranceWidth = Scale * 2f;
        WallAlongZ("Wall Toilets W", ToiletsX0, ToiletsZ0, z1, (entranceZ, entranceWidth, doorHeight));
        WallAlongX("Wall Toilets S", ToiletsZ0, ToiletsX0, x1);
        WallAlongX("Wall Toilets N", z1, ToiletsX0, x1);
        // Le mur est est le mur ouest de l'amphi (BuildShell)

        Sign("TOILETTES", new Vector3(ToiletsX0 - WallThickness / 2f - 0.02f, (doorHeight + WallHeight) / 2f, entranceZ), 0.18f, MenuStyle.Text * 0.95f, 90f);

        // Ascenseur (coin nord-ouest), porte sur le couloir ouest
        BuildElevator();

        // Lavabos + miroirs : le long du mur de droite en entrant (mur sud) et du mur de gauche (mur de l'ascenseur)
        void Sinks(float sx0, float sx1, float wallZ, float facing)
        {
            float sinkZ = wallZ + facing * (WallThickness / 2f + 0.25f);
            Block("Sinks", new Vector3((sx0 + sx1) / 2f, 0.42f, sinkZ), new Vector3(sx1 - sx0, 0.85f, 0.5f), sanitary);
            Block("Mirror", new Vector3((sx0 + sx1) / 2f, 1.55f, wallZ + facing * (WallThickness / 2f + 0.02f)), new Vector3(sx1 - sx0 - 0.2f, 0.9f, 0.03f), screen);
        }
        const float stallsDepth = 2f;
        Sinks(ToiletsX0 + 0.6f, x1 - stallsDepth - 0.6f, ToiletsZ0, 1f);
        Sinks(ToiletsX0 + 0.4f, ElevatorX1 - 0.4f, ElevatorZ0, -1f);

        // Cabines de 1,6 m x 2 m : cloisons basses, une cuvette au fond de chaque cabine
        const float stallWidth = 1.6f, stallDepth = 2f;
        float stallX0 = x1 - stallDepth;
        int stalls = Mathf.FloorToInt((z1 - ToiletsZ0) / stallWidth);
        for (int i = 0; i < stalls; i++)
        {
            float z = ToiletsZ0 + i * stallWidth;
            if (i > 0)
                Block("Stall Wall", new Vector3((stallX0 + x1) / 2f, 1f, z), new Vector3(stallDepth, 2f, 0.06f), wall);
            Block("Toilet", new Vector3(x1 - 0.45f, 0.22f, z + stallWidth / 2f), new Vector3(0.6f, 0.45f, 0.42f), sanitary);
        }
    }

    // Cage d'ascenseur fermée (vers l'étage 1) : portes métalliques fermées côté couloir, bouton d'appel, toit
    const float ElevatorDepth = 3f, ElevatorWidth = 5f; // en x (vers l'est) / en z (le long du couloir, côté porte)
    const float ElevatorX1 = ToiletsX0 + ElevatorDepth, ElevatorZ0 = AmphiZ1 - ElevatorWidth;

    static void BuildElevator()
    {
        WallAlongX("Wall Elevator S", ElevatorZ0, ToiletsX0, ElevatorX1);
        WallAlongZ("Wall Elevator E", ElevatorX1, ElevatorZ0, AmphiZ1);
        Block("Elevator Roof", new Vector3((ToiletsX0 + ElevatorX1) / 2f, WallHeight + 0.05f, (ElevatorZ0 + AmphiZ1) / 2f),
            new Vector3(ElevatorDepth + WallThickness, 0.1f, ElevatorWidth + WallThickness), wall);

        // Portes (deux battants coulissants fermés) et encadrement, sur la face ouest du mur
        float doorZ = (ElevatorZ0 + AmphiZ1) / 2f, faceX = ToiletsX0 - WallThickness / 2f;
        const float leafWidth = 0.8f, leafHeight = 2.1f;
        Block("Elevator Frame", new Vector3(faceX - 0.02f, (leafHeight + 0.1f) / 2f, doorZ), new Vector3(0.04f, leafHeight + 0.1f, leafWidth * 2f + 0.2f), monitor);
        foreach (float side in new[] { -1f, 1f })
            Block("Elevator Door", new Vector3(faceX - 0.05f, leafHeight / 2f, doorZ + side * (leafWidth / 2f + 0.005f)),
                new Vector3(0.03f, leafHeight, leafWidth - 0.01f), metal);
        Block("Elevator Button", new Vector3(faceX - 0.04f, 1.1f, doorZ + leafWidth + 0.35f), new Vector3(0.04f, 0.16f, 0.09f), accent);
        Sign("ASCENSEUR", new Vector3(faceX - 0.03f, (leafHeight + WallHeight) / 2f + 0.05f, doorZ), 0.16f, MenuStyle.Text * 0.95f, 90f);
    }

    // Amphithéâtre : estrade + grand écran contre le mur est, rangées de bureaux tournées vers l'est avec une allée au centre
    static void BuildAmphitheater()
    {
        float centerZ = (FrontZ + AmphiZ1) / 2f;
        Block("Amphi Floor", new Vector3((AmphiX0 + MaxX) / 2f, -0.035f, centerZ), new Vector3(MaxX - AmphiX0, 0.1f, AmphiZ1 - FrontZ), roomFloor);
        Block("Stage", new Vector3(MaxX - StageDepth / 2f, 0.125f, centerZ), new Vector3(StageDepth - WallThickness, 0.25f, AmphiZ1 - FrontZ - WallThickness), stage);

        // Grand écran : aussi large que possible, sous le haut du mur
        float wallX = MaxX - WallThickness / 2f;
        float screenWidth = Mathf.Min(12f, (AmphiZ1 - FrontZ) * 0.6f);
        Block("Screen", new Vector3(wallX - 0.03f, 1.55f, centerZ), new Vector3(0.05f, 1.9f, screenWidth), screen);
        Sign("SUPINFO", new Vector3(wallX - 0.07f, 1.75f, centerZ), 0.5f, Color.white, 90f);
        Sign("AMPHITHÉÂTRE", new Vector3(wallX - 0.07f, 1.2f, centerZ), 0.2f, new Color(1f, 1f, 1f, 0.8f), 90f);
        Display("Screen Display", new Vector3(wallX - 0.1f, 1.55f, centerZ), 90f, 1.7f, true);

        Desk(DeskBlack, new Vector3(MaxX - StageDepth / 2f, 0.25f, centerZ), -90f, 1);

        // Rangées entre le couloir d'entrée (côté ouest) et l'estrade ; places de part et d'autre de l'allée centrale
        float[] rowsX = Spread(AmphiX0 + 5f, MaxX - StageDepth - 2f, AmphiRowSpacing);
        float[] seatsZ = Spread(FrontZ + 1.3f, centerZ - AmphiAisle / 2f, AmphiSeatSpacing)
            .Concat(Spread(centerZ + AmphiAisle / 2f, AmphiZ1 - 1.3f, AmphiSeatSpacing)).ToArray();
        for (int row = 0; row < rowsX.Length; row++)
            foreach (float z in seatsZ)
                Desk(DeskWhite, new Vector3(rowsX[row], 0f, z), 90f, row, true);
    }

    // Salle de pause : espace ouvert (passage libre) entre l'escalier / les toilettes et la rangée de salles du fond.
    // Renfoncement entre le stockage et S.01 : grande table ronde au centre, tables rectangulaires collées entre elles
    // contre le mur de S.01, et une rangée de tables collées contre le mur du stockage
    static void BuildBreakRoom()
    {
        const float roundDiameter = 4f, tableLength = 1.8f, tableDepth = 0.9f;
        float centerX = (StorageX1 + S01X0) / 2f, centerZ = (BackRowZ + BackZ) / 2f;
        RoundTable(new Vector3(centerX, 0f, centerZ), roundDiameter);
        Sign("SALLE DE PAUSE", new Vector3(centerX, 2f, BackZ - WallThickness / 2f - 0.02f), 0.24f, MenuStyle.Text * 0.95f);

        // Contre le mur ouest de S.01 : tables tournées de 90° (le long du mur)
        float wallX = S01X0 - WallThickness / 2f - tableDepth / 2f;
        foreach (float z in Glued(BackRowZ + 1f, BackZ - 1f, tableLength))
            RectTable(new Vector3(wallX, 0f, z), tableDepth, tableLength);

        // Au fond, derrière la table ronde : à gauche des frigos, puis des tables collées contre le mur avec un micro-ondes
        float backZ = BackZ - WallThickness / 2f - tableDepth / 2f;
        float[] backX = Glued(StorageX1 + WallThickness / 2f + 0.3f, wallX - tableDepth / 2f - 0.2f, tableLength);
        const int fridgeTables = 2; // places de table prises par les frigos
        const float fridgeWidth = 0.85f;
        float fridgesX0 = backX[0] - tableLength / 2f, fridgesX1 = backX[fridgeTables - 1] + tableLength / 2f;
        foreach (float x in Glued(fridgesX0, fridgesX1, fridgeWidth))
            Fridge(new Vector3(x, 0f, BackZ - WallThickness / 2f), fridgeWidth);
        foreach (float x in backX.Skip(fridgeTables))
        {
            RectTable(new Vector3(x, 0f, backZ), tableLength, tableDepth);
            Microwave(new Vector3(x, 0.75f, backZ + 0.1f));
        }

        // Contre le mur du stockage, côté salle de pause
        float storageZ = BackRowZ - WallThickness / 2f - tableDepth / 2f;
        foreach (float x in Glued(MinX + 0.5f, StorageX1 - 0.5f, tableLength))
            RectTable(new Vector3(x, 0f, storageZ), tableLength, tableDepth);
    }

    // Frigo dos au mur (wallZ = face du mur, frigo vers le sud) : caisse, joint entre congélateur et frigo, poignées
    static void Fridge(Vector3 wallPosition, float width)
    {
        const float height = 1.85f, depth = 0.7f;
        var size = new Vector3(width - 0.03f, height, depth);
        Vector3 center = wallPosition + new Vector3(0f, height / 2f, -depth / 2f);
        Block("Fridge", center, size, appliance, "Break Room");
        float front = wallPosition.z - depth - 0.006f;
        Block("Fridge Seam", new Vector3(center.x, 1.3f, front), new Vector3(size.x, 0.015f, 0.01f), monitor, "Break Room");
        float handleX = center.x + size.x / 2f - 0.08f;
        Block("Fridge Handle", new Vector3(handleX, 1.55f, front - 0.02f), new Vector3(0.03f, 0.3f, 0.03f), metal, "Break Room");
        Block("Fridge Handle", new Vector3(handleX, 0.95f, front - 0.02f), new Vector3(0.03f, 0.5f, 0.03f), metal, "Break Room");
    }

    // Micro-ondes posé sur une table (position = milieu du dessous), porte vitrée tournée vers le sud
    static void Microwave(Vector3 position)
    {
        var size = new Vector3(0.5f, 0.3f, 0.36f);
        Block("Microwave", position + Vector3.up * size.y / 2f, size, appliance, "Break Room");
        float front = position.z - size.z / 2f - 0.005f;
        Block("Microwave Door", new Vector3(position.x - 0.05f, position.y + size.y / 2f, front), new Vector3(0.32f, 0.22f, 0.01f), monitor, "Break Room");
        Block("Microwave Panel", new Vector3(position.x + 0.18f, position.y + size.y / 2f, front), new Vector3(0.08f, 0.22f, 0.01f), metal, "Break Room");
    }

    // Table rectangulaire : plateau + 4 pieds
    static void RectTable(Vector3 position, float sizeX, float sizeZ)
    {
        const float height = 0.75f, top = 0.05f, leg = 0.06f;
        Block("Table", position + Vector3.up * (height - top / 2f), new Vector3(sizeX, top, sizeZ), wood, "Break Room");
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Block("Table Leg", position + new Vector3(sx * (sizeX / 2f - leg), (height - top) / 2f, sz * (sizeZ / 2f - leg)),
                new Vector3(leg, height - top, leg), monitor, "Break Room");
    }

    // Table ronde (plateau + pied central), collision sur toute la surface du plateau
    static void RoundTable(Vector3 position, float diameter)
    {
        const float height = 0.75f, topThickness = 0.06f;
        var top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        top.name = "Round Table";
        top.transform.SetParent(Group("Break Room"), false);
        top.transform.position = position + Vector3.up * (height - topThickness / 2f);
        top.transform.localScale = new Vector3(diameter, topThickness / 2f, diameter); // cylindre Unity : 2 m de haut
        top.GetComponent<MeshRenderer>().sharedMaterial = wood;
        Object.DestroyImmediate(top.GetComponent<Collider>());
        top.AddComponent<MeshCollider>().convex = true;
        top.isStatic = true;

        var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        leg.name = "Round Table Leg";
        leg.transform.SetParent(top.transform.parent, false);
        leg.transform.position = position + Vector3.up * (height - topThickness) / 2f;
        leg.transform.localScale = new Vector3(diameter * 0.3f, (height - topThickness) / 2f, diameter * 0.3f);
        leg.GetComponent<MeshRenderer>().sharedMaterial = monitor;
        leg.isStatic = true;
    }

    // Salle de stockage : vide et fermée (pas de porte), un plafond la cache depuis la vue du dessus
    static void BuildStorage()
    {
        Block("Storage Ceiling", new Vector3((MinX + StorageX1) / 2f, WallHeight + 0.05f, (BackRowZ + BackZ) / 2f),
            new Vector3(StorageX1 - MinX + WallThickness, 0.1f, BackZ - BackRowZ + WallThickness), wall);
    }

    // Bureau administratif entre S.01 et S.03 (on entre par le sud) :
    // 2 bureaux collés contre le mur de droite (est, dos au mur), et au fond 2 paires de bureaux collés, tournées vers la porte
    static void BuildOffice()
    {
        float centerZ = (BackRowZ + BackZ) / 2f;
        Block("Office Floor", new Vector3((S01X1 + OfficeX1) / 2f, -0.035f, centerZ), new Vector3(OfficeX1 - S01X1, 0.1f, BackZ - BackRowZ), roomFloor);
        float[] pairZ = { centerZ - deskWidth / 2f, centerZ + deskWidth / 2f };
        int variant = 0;

        // Contre le mur est : on tourne le dos au mur (dossier des chaises contre le mur), on regarde vers l'ouest
        const float chairDepth = 0.6f;
        float wallDeskX = OfficeX1 - WallThickness / 2f - chairDepth - ChairGap - deskDepth / 2f;
        foreach (float z in pairZ)
            Desk(DeskBlue, new Vector3(wallDeskX, 0f, z), -90f, variant++);

        // Au fond de la salle, face à la porte : 2 paires de bureaux collés, on y est assis tourné vers l'entrée (sud)
        float backZ = BackZ - WallThickness / 2f - 1.7f;
        float[] pairsX = { S01X1 + 2.5f + deskWidth, wallDeskX - deskDepth / 2f - 1.5f - deskWidth };
        foreach (float pairX in pairsX)
        foreach (float side in new[] { -0.5f, 0.5f })
            Desk(DeskBlue, new Vector3(pairX + side * deskWidth, 0f, backZ), 180f, variant++);
    }

    // Salle de cours entre x0 et x1 dans la rangée du fond : porte au sud, tableau sur le mur ouest,
    // rangées de bureaux collés les uns aux autres, tournées vers le tableau (vers l'ouest)
    static void BuildClassroom(string label, float x0, float x1)
    {
        float centerX = (x0 + x1) / 2f, z0 = BackRowZ, z1 = BackZ, centerZ = (z0 + z1) / 2f;
        Block(label + " Floor", new Vector3(centerX, -0.035f, centerZ), new Vector3(x1 - x0, 0.1f, z1 - z0), roomFloor);

        float wallX = x0 + WallThickness / 2f;
        float boardHeight = Mathf.Min(1.4f, (z1 - z0) / 4.5f);
        Block(label + " Board", new Vector3(wallX + 0.03f, 1.45f, centerZ), new Vector3(0.05f, 1.5f, Mathf.Min(6f, z1 - z0 - 2f)), board);
        Sign(label.ToUpperInvariant(), new Vector3(wallX + 0.07f, 1.45f, centerZ), 0.22f, new Color(1f, 1f, 1f, 0.85f), -90f);
        // Le partage de l'organisateur s'affiche sur le tableau (par-dessus le nom de la salle)
        Display(label + " Board Display", new Vector3(wallX + 0.1f, 1.45f, centerZ), -90f, boardHeight, true);

        // Bureau du prof devant le tableau, tourné vers les élèves
        Desk(DeskBlack, new Vector3(x0 + 2.2f, 0f, centerZ), 90f, 2);

        // Rangées (à x fixe) de bureaux collés, en laissant le passage devant la porte (mur sud)
        float rowZ0 = z0 + 2f, rowZ1 = z1 - 1.2f;
        int perRow = Mathf.Max(1, Mathf.FloorToInt((rowZ1 - rowZ0) / deskWidth));
        float[] rowsX = Spread(x0 + 4.4f, x1 - 1.4f, ClassRowSpacing);
        for (int row = 0; row < rowsX.Length; row++)
        for (int i = 0; i < perRow; i++)
        {
            float z = (rowZ0 + rowZ1) / 2f + (i - (perRow - 1) / 2f) * deskWidth;
            Desk(DeskWhite, new Vector3(rowsX[row], 0f, z), -90f, row + i, true);
        }
    }

    // Rue Colson devant la façade (zone d'apparition des joueurs autour de l'origine) et immeubles voisins
    static void BuildStreet()
    {
        const float streetX0 = MinX - 50f, streetX1 = MaxX + 30f;
        float streetCenter = (streetX0 + streetX1) / 2f, streetLength = streetX1 - streetX0;
        Block("Sidewalk", new Vector3(streetCenter, -0.03f, (SidewalkZ0 + FrontZ) / 2f), new Vector3(streetLength, 0.08f, FrontZ - SidewalkZ0), sidewalk);
        Block("Road", new Vector3(streetCenter, -0.035f, (RoadZ0 + SidewalkZ0) / 2f), new Vector3(streetLength, 0.08f, SidewalkZ0 - RoadZ0), road);
        for (float x = streetX0 + 2f; x < streetX1; x += 6f)
            Block("Road Marking", new Vector3(x, 0.006f, (RoadZ0 + SidewalkZ0) / 2f), new Vector3(3f, 0.01f, 0.15f), marking);

        // Plaque de rue, hors de la zone d'apparition (rayon ~5 m autour de l'origine)
        const float plateX = -7f, plateZ = SidewalkZ0 + 1.2f;
        Block("Street Sign Post", new Vector3(plateX, 1.25f, plateZ), new Vector3(0.08f, 2.5f, 0.08f), monitor);
        Block("Street Sign Plate", new Vector3(plateX, 2.55f, plateZ), new Vector3(2.4f, 0.45f, 0.06f), accent);
        Sign("RUE COLSON", new Vector3(plateX, 2.55f, plateZ - 0.04f), 0.2f, Color.white);

        Place(Props + "dprop_bicycle.prefab", new Vector3(EntranceX + EntranceWidth / 2f + 6f, 0f, FrontZ - 0.8f), 80f, "Street");
        Place(Props + "dprop_plantPot.prefab", new Vector3(EntranceX - EntranceWidth / 2f - 1f, 0f, FrontZ - 0.4f), 0f, "Street");
        Place(Props + "dprop_plantPot.prefab", new Vector3(EntranceX + EntranceWidth / 2f + 1f, 0f, FrontZ - 0.4f), 0f, "Street");

        Place(Props + "building_block_A.prefab", new Vector3(MinX - 14f, 0f, RoadZ0 - 10f), 0f, "Street");
        Place(Props + "building_block_B.prefab", new Vector3((MinX + MaxX) / 2f - 10f, 0f, RoadZ0 - 12f), 0f, "Street");
        Place(Props + "building_block_C.prefab", new Vector3(MaxX - 6f, 0f, RoadZ0 - 10f), 0f, "Street");
        Place(Props + "building_block_D.prefab", new Vector3(MinX - 10f, 0f, (FrontZ + BackZ) / 2f), 90f, "Street");
    }

    // Plafond + dalles lumineuses, visibles seulement en première personne (FirstPersonOnly) : en vue du dessus
    // on voit toujours l'intérieur. Pas de lampes temps réel (trop de lumières sur les grands sols, ça scintille) :
    // le plafond ne projette pas d'ombre, l'intérieur reste éclairé par le soleil et la lumière ambiante.
    // Pas de collision ; ouvert au-dessus de l'escalier (pour l'étage 1).
    static void BuildCeiling()
    {
        Transform group = Group("Ceiling");
        group.gameObject.AddComponent<FirstPersonOnly>();
        const float thickness = 0.05f;
        float y = WallHeight - thickness / 2f;

        void Panel(string name, float x0, float x1, float z0, float z1)
        {
            Renderer r = Part(name, PrimitiveType.Cube, new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f), Quaternion.identity,
                new Vector3(x1 - x0, thickness, z1 - z0), ceiling, group);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Panel("Ceiling Front", MinX, MaxX, FrontZ, StairsZ0);
        Panel("Ceiling Back", MinX, MaxX, StairsZ1, BackZ);
        Panel("Ceiling Middle", StairsX1, MaxX, StairsZ0, StairsZ1);

        // Dalles lumineuses : une grille par pièce, à distance des murs
        var rooms = new (float x0, float x1, float z0, float z1)[]
        {
            (MinX, AmphiX0, FrontZ, ToiletsZ0),                  // hall
            (MinX, StairsX1, ToiletsZ0, StairsZ0),               // entre le comptoir et l'escalier
            (StairsX1, ToiletsX0, ToiletsZ0, AmphiZ1),           // couloir le long des toilettes
            (ToiletsX0, AmphiX0, ToiletsZ0, ElevatorZ0),         // toilettes (hors ascenseur)
            (ElevatorX1, AmphiX0, ElevatorZ0, AmphiZ1),
            (AmphiX0, MaxX, FrontZ, AmphiZ1),                    // amphithéâtre
            (StairsX1, MaxX, AmphiZ1, BackRowZ),                 // salle de pause
            (MinX, StairsX1, StairsZ1, BackRowZ),
            (StorageX1, S01X0, BackRowZ, BackZ),                 // renfoncement de la grande table
            (S01X0, S01X1, BackRowZ, BackZ),                     // S.01
            (S01X1, OfficeX1, BackRowZ, BackZ),                  // bureau
            (OfficeX1, MaxX, BackRowZ, BackZ),                   // S.03
        };
        const float spacing = 4.5f, margin = 1.2f;
        foreach (var room in rooms)
        foreach (float x in Spread(room.x0 + margin, room.x1 - margin, spacing))
        foreach (float z in Spread(room.z0 + margin, room.z1 - margin, spacing))
        {
            Renderer r = Part("Ceiling Light", PrimitiveType.Cube, new Vector3(x, y - thickness / 2f - 0.015f, z), Quaternion.identity,
                new Vector3(1.2f, 0.03f, 0.6f), ceilingLight, group);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Masqués dans la scène enregistrée (vue du dessus dans l'éditeur) ; FirstPersonOnly les affiche en jeu
        foreach (Renderer r in group.GetComponentsInChildren<Renderer>()) r.enabled = false;
    }

    // ---------- Mobilier ----------    // ---------- Mobilier ----------

    // Bureau + chaise (siège). yaw = direction vers laquelle regarde la personne assise ; la chaise est placée derrière.
    // withMonitor : écran de PC sur le bureau, tourné vers la chaise.
    static void Desk(string deskPath, Vector3 position, float yaw, int chairVariant, bool withMonitor = false)
    {
        GameObject desk = Place(deskPath, position, yaw + DeskYaw, "Furniture");
        Bounds bounds = WorldBounds(desk);
        float depth = bounds.size.z;
        if (Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(yaw, -90f)) < 45f)
            depth = bounds.size.x;

        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 chairPosition = position + rotation * Vector3.back * (depth / 2f + ChairGap);
        GameObject chair = Place(Chairs[Mathf.Abs(chairVariant) % Chairs.Length], chairPosition, yaw + ChairYaw, "Furniture");
        Seat seat = null;
        if (chair != null)
        {
            seat = chair.AddComponent<Seat>();
            seat.index = seatCount++;
            seat.point = chairPosition;
            seat.yaw = yaw;
            seat.deskHeight = bounds.max.y - position.y;
            seat.deskDistance = ChairGap;
        }

        if (withMonitor)
        {
            // Pied + écran au fond du bureau, à hauteur des yeux d'une personne assise
            Vector3 top = new Vector3(position.x, bounds.max.y, position.z) + rotation * Vector3.forward * (depth * 0.2f);
            Part("Monitor Base", PrimitiveType.Cube, top + Vector3.up * 0.01f, rotation, new Vector3(0.22f, 0.02f, 0.16f), monitor);
            Part("Monitor Stand", PrimitiveType.Cube, top + Vector3.up * 0.12f, rotation, new Vector3(0.04f, 0.2f, 0.03f), monitor);
            ScreenDisplay screenDisplay = Display("Monitor", top + Vector3.up * 0.34f - rotation * Vector3.forward * 0.03f, yaw, 0.33f, false);
            if (seat != null) seat.monitor = screenDisplay;
        }
    }

    // Écran 16:9 qui affiche le partage de l'organisateur, vu par quelqu'un qui regarde vers yaw.
    // Moniteur (bigScreen = false) : avec un cadre noir. Tableau / grand écran : seulement l'image, masquée sans partage,
    // et la caméra à côté au lieu d'être incrustée.
    static ScreenDisplay Display(string name, Vector3 center, float yaw, float height, bool bigScreen)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right;
        float width = height * 16f / 9f;

        var go = new GameObject(name);
        go.transform.SetParent(Group("Screens"), false);
        go.transform.SetPositionAndRotation(center, rotation);
        var screenDisplay = go.AddComponent<ScreenDisplay>();

        if (!bigScreen)
            Part("Frame", PrimitiveType.Cube, center + forward * 0.02f, rotation, new Vector3(width + 0.04f, height + 0.04f, 0.03f), monitor, go.transform);

        screenDisplay.main = Part("Image", PrimitiveType.Quad, center, rotation, new Vector3(width, height, 1f), display, go.transform);
        float camHeight = bigScreen ? height * 0.5f : height * 0.32f;
        Vector3 camCenter = bigScreen
            ? center + right * (width / 2f + 0.1f + camHeight * 2f / 3f) - Vector3.up * (height - camHeight) / 2f
            : center + right * (width - camHeight * 4f / 3f) / 2f - Vector3.up * (height - camHeight) / 2f - forward * 0.002f;
        screenDisplay.inset = Part("Camera", PrimitiveType.Quad, camCenter, rotation, new Vector3(camHeight * 4f / 3f, camHeight, 1f), display, go.transform);
        screenDisplay.main.enabled = screenDisplay.inset.enabled = false;
        return screenDisplay;
    }

    // Primitive sans collision (décor posé sur un meuble ou un mur)
    static Renderer Part(string name, PrimitiveType type, Vector3 position, Quaternion rotation, Vector3 scale, Material material, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent != null ? parent : Group("Screens"), false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    // Instancie un prefab posé au sol (le bas de l'objet à la hauteur position.y), centré sur position en X/Z
    static GameObject Place(string path, Vector3 position, float yaw, string group)
    {
        GameObject prefab = Load(path);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Group(group));
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        Bounds b = WorldBounds(go);
        if (b.size != Vector3.zero)
            go.transform.position += new Vector3(position.x - b.center.x, position.y - b.min.y, position.z - b.center.z);
        return go;
    }

    static GameObject Load(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) Debug.LogWarning("Asset introuvable : " + path);
        return prefab;
    }

    static Bounds WorldBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    static Transform Group(string name)
    {
        Transform group = root.Find(name);
        if (group == null)
        {
            group = new GameObject(name).transform;
            group.SetParent(root, false);
        }
        return group;
    }

    // ---------- Primitives ----------

    static void Block(string name, Vector3 center, Vector3 size, Material material, string group = "Building")
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(Group(group), false);
        go.transform.position = center;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        go.isStatic = true;
    }

    // Texte 3D lisible par quelqu'un qui regarde vers yaw (0 : depuis le sud, comme la caméra vue du dessus)
    static void Sign(string text, Vector3 position, float height, Color color, float yaw = 0f)
    {
        var go = new GameObject("Sign " + text);
        go.transform.SetParent(Group("Signs"), false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = MenuStyle.Font;
        mesh.fontSize = 96;
        mesh.fontStyle = FontStyle.Bold;
        mesh.characterSize = height * 10f / mesh.fontSize; // characterSize 1 ≈ 0,1 m par point de police
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = signText;
    }

    static void CreateMaterials()
    {
        AnimationTestSceneBuilder.EnsureFolder(MaterialsDir);
        AnimationTestSceneBuilder.EnsureFolder(TexturesDir);
        Texture2D plaster = PatternTexture("Plaster", Plaster), tiles = PatternTexture("Tiles", Tiles),
            carpet = PatternTexture("Carpet", Carpet),
            weave = PatternTexture("Fabric", Weave), asphalt = PatternTexture("Asphalt", Asphalt);

        wall = MapMaterial("Wall", Color.white, Color.black, 0.05f, plaster, 2f);
        hallFloor = MapMaterial("FloorHall", new Color32(0x8C, 0x91, 0x9C, 0xFF), Color.black, 0.5f);
        roomFloor = MapMaterial("FloorRooms", new Color32(0x5C, 0x66, 0x7D, 0xFF), Color.black, 0.1f, carpet, 1.5f);
        board = MapMaterial("Board", new Color32(0x1B, 0x2A, 0x24, 0xFF), Color.black, 0.2f);
        screen = MapMaterial("Screen", new Color32(0x10, 0x1C, 0x48, 0xFF), MenuStyle.Accent * 0.8f, 0.8f);
        stage = MapMaterial("Stage", new Color32(0xE6, 0xE6, 0xE3, 0xFF), Color.black, 0.35f);
        wood = MapMaterial("Wood", new Color32(0xF5, 0xF5, 0xF3, 0xFF), Color.black, 0.35f); // tables, comptoir, escalier : blanc uni
        doorBlue = DoorMaterial(new Color32(0x2F, 0x5D, 0xB0, 0xFF));
        fabric = MapMaterial("Fabric", new Color32(0x3C, 0x5F, 0x9E, 0xFF), Color.black, 0.05f, weave, 0.25f);
        accent = MapMaterial("Accent", MenuStyle.Accent, MenuStyle.Accent * 0.6f, 0.5f);
        monitor = MapMaterial("Monitor", new Color32(0x16, 0x18, 0x1F, 0xFF), Color.black, 0.6f);
        metal = MapMaterial("Metal", new Color32(0xB4, 0xB9, 0xC1, 0xFF), Color.black, 0.75f);
        appliance = MapMaterial("Appliance", new Color32(0xEC, 0xED, 0xEF, 0xFF), Color.black, 0.5f);
        tile = MapMaterial("Tile", new Color32(0xC9, 0xD1, 0xDA, 0xFF), Color.black, 0.7f, tiles, 0.3f);
        sanitary = MapMaterial("Sanitary", new Color32(0xF4, 0xF6, 0xF8, 0xFF), Color.black, 0.8f);
        road = MapMaterial("Road", new Color32(0x3A, 0x3C, 0x41, 0xFF), Color.black, 0.15f, asphalt, 3f);
        sidewalk = MapMaterial("Sidewalk", new Color32(0xA0, 0xA3, 0xAA, 0xFF), Color.black, 0.1f, tiles, 0.5f);
        marking = MapMaterial("Marking", new Color32(0xF2, 0xF2, 0xF2, 0xFF), Color.black, 0.2f);
        ceiling = MapMaterial("Ceiling", new Color32(0xEE, 0xEF, 0xF2, 0xFF), Color.black, 0.1f, tiles, 0.6f);
        ceilingLight = MapMaterial("CeilingLight", new Color32(0xFF, 0xFB, 0xF0, 0xFF), new Color(1.1f, 1.08f, 1.02f), 0.5f);
        display = DisplayMaterial();
        signText = SignMaterial();
    }

    // ---------- Textures générées (motifs en niveaux de gris, teintés par la couleur du matériau, raccordables) ----------

    const string TexturesDir = MaterialsDir + "/Textures";
    const int TextureSize = 256;

    // Texture enregistrée en PNG dans le projet (régénérée à chaque construction de la carte)
    static Texture2D PatternTexture(string name, System.Func<float, float, float> pattern)
    {
        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGB24, false);
        var pixels = new Color32[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
        for (int x = 0; x < TextureSize; x++)
        {
            byte v = (byte)(Mathf.Clamp01(pattern((float)x / TextureSize, (float)y / TextureSize)) * 255f);
            pixels[y * TextureSize + x] = new Color32(v, v, v, 255);
        }
        tex.SetPixels32(pixels);
        string path = TexturesDir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Bruit de valeur périodique (cellsU x cellsV cases sur la texture), pour des motifs sans couture
    static float Noise(float u, float v, int cellsU, int cellsV, int seed)
    {
        float x = u * cellsU, y = v * cellsV;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(x0, y0, cellsU, cellsV, seed), b = Hash(x0 + 1, y0, cellsU, cellsV, seed);
        float c = Hash(x0, y0 + 1, cellsU, cellsV, seed), d = Hash(x0 + 1, y0 + 1, cellsU, cellsV, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Hash(int x, int y, int cellsU, int cellsV, int seed)
    {
        x = ((x % cellsU) + cellsU) % cellsU;
        y = ((y % cellsV) + cellsV) % cellsV;
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    // Somme de bruits de plus en plus fins (0..1)
    static float Fbm(float u, float v, int cells, int seed, int octaves = 4)
    {
        float sum = 0f, amplitude = 0.5f, total = 0f;
        for (int i = 0; i < octaves; i++, cells *= 2, amplitude *= 0.5f)
        {
            sum += Noise(u, v, cells, cells, seed + i) * amplitude;
            total += amplitude;
        }
        return sum / total;
    }

    static float Plaster(float u, float v) => 0.96f + 0.04f * Fbm(u, v, 8, 1);

    // Un carreau par texture, joints sur les bords
    static float Tiles(float u, float v)
    {
        float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
        if (edge < 0.015f) return 0.62f;
        return 0.9f + 0.1f * Fbm(u, v, 4, 2);
    }

    static float Carpet(float u, float v) => 0.78f + 0.22f * Fbm(u, v, 32, 3, 3);

    // Tissage serré
    static float Weave(float u, float v)
    {
        float threads = Mathf.Sin(u * Mathf.PI * 2f * 48f) * Mathf.Sin(v * Mathf.PI * 2f * 48f);
        return 0.82f + 0.08f * Mathf.Sign(threads) + 0.1f * Fbm(u, v, 16, 6, 2);
    }

    static float Asphalt(float u, float v)
    {
        float grain = Fbm(u, v, 32, 7, 3);
        float speckle = Hash(Mathf.FloorToInt(u * TextureSize), Mathf.FloorToInt(v * TextureSize), TextureSize, TextureSize, 8) > 0.97f ? 0.25f : 0f;
        return 0.7f + 0.25f * grain + speckle;
    }

    // Matériau des portes : copie du bois blanc du pack WoodDoors (garde sa texture et son relief), teintée
    static Material DoorMaterial(Color color)
    {
        string path = MaterialsDir + "/DoorBlue.mat";
        var source = AssetDatabase.LoadAssetAtPath<Material>(ThirdParty + "WoodDoors/Materials/Door_1_Materials/Door_1_White.mat");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (source != null)
        {
            mat.CopyPropertiesFromMaterial(source);
        }
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Matériau des écritures : texture de la police, shader Campus/Text3D (caché par les murs)
    static Material SignMaterial()
    {
        string path = MaterialsDir + "/SignText.mat";
        Shader shader = Shader.Find("Campus/Text3D");
        if (shader == null) return MenuStyle.Font.material;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.mainTexture = MenuStyle.Font.material.mainTexture;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Image des écrans : sans éclairage, pour rester lisible dans une salle sombre (texture donnée en jeu par ClassStream)
    static Material DisplayMaterial()
    {
        string path = MaterialsDir + "/Display.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Unlit/Texture"));
            AssetDatabase.CreateAsset(mat, path);
        }
        return mat;
    }

    // Matériau du campus. Avec une texture : shader Campus/Triplanar (motif de taille fixe en mètres, quelle que soit
    // la taille du bloc) ; sans texture : Standard, couleur unie.
    static Material MapMaterial(string name, Color color, Color emission, float smoothness, Texture2D texture = null, float tiling = 1f)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        Shader triplanar = texture != null ? Shader.Find("Campus/Triplanar") : null;
        Shader shader = triplanar != null ? triplanar : Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.color = color;
        mat.SetFloat("_Glossiness", smoothness);
        if (triplanar != null)
        {
            mat.SetTexture("_MainTex", texture);
            mat.SetFloat("_Tiling", tiling);
        }
        else
        {
            mat.SetTexture("_MainTex", null);
        }
        if (emission.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
