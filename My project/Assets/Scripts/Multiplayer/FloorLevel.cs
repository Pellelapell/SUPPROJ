using System.Collections.Generic;
using UnityEngine;

// Étage du campus (rez-de-chaussée, étages 1 et 4) dans la scène de jeu. En vue du dessus, seul l'étage où se trouve
// le joueur local est affiché (l'étage du dessus cacherait le sien), ainsi que les autres joueurs de cet étage ;
// en première personne, tous les étages et tous les joueurs sont affichés (on voit par la cage d'escalier, les vitres…).
// Les collisions restent actives partout. Les objets sous un AlwaysVisible (escaliers) suivent ses étages à lui.
// Placé par CampusMapBuilder sur la racine de chaque étage.
public class FloorLevel : MonoBehaviour
{
    [Tooltip("Numéro de l'étage (0 : rez-de-chaussée)")]
    public int level;
    [Tooltip("Altitude du sol de l'étage")]
    public float baseHeight;

    // On est à un étage dès qu'on est à moins de cette hauteur sous son sol (en haut de l'escalier)
    const float Threshold = 1f;

    public static int Current { get; private set; }

    // Première personne (caméra presque aux yeux) : tout est affiché
    public static bool ShowAll { get; private set; }
    const float FirstPersonShowAll = 0.8f;

    // Deux altitudes au même étage (chaises, portes, ascenseurs à portée seulement à son étage)
    public static bool SameHeight(float a, float b) => Mathf.Abs(a - b) < 1.5f;

    static readonly List<FloorLevel> all = new List<FloorLevel>();
    static int updatedFrame = -1;
    // Rendus des autres joueurs et leur état affiché, pour ne toucher qu'aux joueurs qui changent d'étage
    static readonly Dictionary<NetworkPlayer, (Renderer[] renderers, bool visible)> players =
        new Dictionary<NetworkPlayer, (Renderer[], bool)>();
    static readonly List<NetworkPlayer> gone = new List<NetworkPlayer>();

    // Groupes de rendus affichés ensemble : ceux de l'étage, puis un par AlwaysVisible
    class RenderGroup
    {
        public AlwaysVisible range; // null : rendus de l'étage
        public readonly List<Renderer> renderers = new List<Renderer>();
        public bool shown = true;
    }
    readonly List<RenderGroup> groups = new List<RenderGroup>();

    void Awake()
    {
        var byRange = new Dictionary<AlwaysVisible, RenderGroup>();
        var own = new RenderGroup();
        groups.Add(own);
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            AlwaysVisible range = r.GetComponentInParent<AlwaysVisible>();
            if (range == null) { own.renderers.Add(r); continue; }
            if (!byRange.TryGetValue(range, out RenderGroup g))
            {
                g = new RenderGroup { range = range };
                byRange[range] = g;
                groups.Add(g);
            }
            g.renderers.Add(r);
        }
    }

    void OnEnable() => all.Add(this);

    void OnDisable()
    {
        all.Remove(this);
        foreach (RenderGroup g in groups) Show(g, true);
    }

    void LateUpdate()
    {
        if (updatedFrame != Time.frameCount)
        {
            updatedFrame = Time.frameCount;
            Transform local = CameraFollow.Instance != null ? CameraFollow.Instance.target : null;
            Current = local != null ? LevelAt(local.position.y) : 0;
            ShowAll = CameraFollow.Instance != null && CameraFollow.Instance.FirstPersonBlend >= FirstPersonShowAll;
            HideOtherPlayers(local);
        }
        foreach (RenderGroup g in groups)
            Show(g, ShowAll || (g.range == null ? level == Current : g.range.VisibleOn(Current)));
    }

    // Étage le plus haut dont le sol est sous (ou juste au-dessus de) cette altitude
    public static int LevelAt(float y)
    {
        int best = 0;
        float bestHeight = float.MinValue;
        foreach (FloorLevel f in all)
            if (y >= f.baseHeight - Threshold && f.baseHeight > bestHeight)
            {
                best = f.level;
                bestHeight = f.baseHeight;
            }
        return best;
    }

    // Est-ce que ce joueur est au même étage que le joueur local ? (pseudos, rendu)
    public static bool SameFloor(Transform player) => LevelAt(player.position.y) == Current;

    static void HideOtherPlayers(Transform local)
    {
        foreach (NetworkPlayer p in NetworkPlayer.All)
        {
            if (p == null || p.transform == local) continue;
            bool visible = ShowAll || SameFloor(p.transform);
            if (players.TryGetValue(p, out var state) && state.visible == visible) continue;
            Renderer[] renderers = state.renderers ?? p.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
                if (r != null) r.forceRenderingOff = !visible;
            players[p] = (renderers, visible);
        }

        // Joueurs partis
        foreach (NetworkPlayer p in players.Keys)
            if (p == null) gone.Add(p);
        foreach (NetworkPlayer p in gone) players.Remove(p);
        gone.Clear();
    }

    // forceRenderingOff : indépendant de Renderer.enabled, que d'autres scripts utilisent (plafond, écrans)
    static void Show(RenderGroup g, bool on)
    {
        if (on == g.shown) return;
        g.shown = on;
        foreach (Renderer r in g.renderers)
            if (r != null) r.forceRenderingOff = !on;
    }
}
