using System.Collections.Generic;
using UnityEngine;

// Porte d'ascenseur d'un étage (placée par CampusMapBuilder, sur la face du mur, tournée vers le couloir).
// Devant la porte, F emmène à l'étage suivant et les chiffres (number de chaque étage) à un étage précis :
// le joueur réapparaît devant la porte de l'ascenseur de cet étage (NetworkPlayer). Les portes s'ouvrent quand
// le joueur local approche (effet local, pas synchronisé).
public class ElevatorStop : MonoBehaviour
{
    [Tooltip("Étage dans la scène (FloorLevel.level)")]
    public int level;
    [Tooltip("Chiffre à taper pour venir à cet étage")]
    public int number;
    [Tooltip("Nom affiché (RDC, Étage 1…)")]
    public string label;
    [Tooltip("Les deux battants (gauche puis droite), qui coulissent sur le côté")]
    public Transform[] leaves;
    public float leafWidth = 0.8f;

    // Distance (horizontale) depuis la porte pour appeler l'ascenseur
    public const float Reach = 1.8f;
    const float OpenSpeed = 1.6f;

    static readonly List<ElevatorStop> all = new List<ElevatorStop>();
    public static IReadOnlyList<ElevatorStop> All => all;

    Vector3[] closedPositions;
    float open;

    // Point d'arrivée, devant la porte, et orientation (dos à l'ascenseur)
    public Vector3 ArrivalPoint => transform.position + transform.forward * 1.1f;
    public Quaternion ArrivalRotation => Quaternion.LookRotation(transform.forward);

    void Awake()
    {
        closedPositions = new Vector3[leaves != null ? leaves.Length : 0];
        for (int i = 0; i < closedPositions.Length; i++)
            if (leaves[i] != null) closedPositions[i] = leaves[i].localPosition;
    }

    void OnEnable()
    {
        all.Add(this);
        all.Sort((a, b) => a.level.CompareTo(b.level));
    }

    void OnDisable() => all.Remove(this);

    public bool InReach(Vector3 position)
    {
        Vector3 d = position - transform.position;
        return Mathf.Abs(d.y) < 1.2f && new Vector2(d.x, d.z).magnitude < Reach;
    }

    public static ElevatorStop Nearest(Vector3 position)
    {
        foreach (ElevatorStop s in all)
            if (s.InReach(position)) return s;
        return null;
    }

    // Étage suivant en montant, puis retour au rez-de-chaussée depuis le dernier
    public ElevatorStop Next
    {
        get
        {
            foreach (ElevatorStop s in all)
                if (s.level > level) return s;
            return all.Count > 0 && all[0] != this ? all[0] : null;
        }
    }

    void Update()
    {
        Transform local = CameraFollow.Instance != null ? CameraFollow.Instance.target : null;
        float target = local != null && InReach(local.position) ? 1f : 0f;
        if (Mathf.Approximately(open, target)) return;
        open = Mathf.MoveTowards(open, target, OpenSpeed * Time.deltaTime);
        for (int i = 0; i < closedPositions.Length; i++)
            if (leaves[i] != null)
                leaves[i].localPosition = closedPositions[i] + Vector3.right * ((i == 0 ? -1f : 1f) * open * leafWidth * 0.95f);
    }
}
