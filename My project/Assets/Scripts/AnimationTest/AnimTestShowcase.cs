using UnityEngine;

// Perso de démo : joue un état de l'Animator en continu et affiche son nom au-dessus de la tête.
[RequireComponent(typeof(Animator))]
public class AnimTestShowcase : MonoBehaviour
{
    public string stateName;         // nom de l'état dans le controller "Showcase"
    public string label;

    Animator animator;
    GUIStyle style;

    void Start()
    {
        animator = GetComponent<Animator>();
        animator.Play(stateName, 0, Random.value);
    }

    void Update()
    {
        // Les clips qui ne bouclent pas (ex : Waving) sont relancés à la fin
        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.loop && info.normalizedTime >= 1f)
            animator.Play(stateName, 0, 0f);
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.1f);
        if (p.z < 0f) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(p.x - 90f, Screen.height - p.y - 12f, 180f, 24f), label, style);
    }
}
