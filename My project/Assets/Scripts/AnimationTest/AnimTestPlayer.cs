using UnityEngine;

// Perso contrôlable pour tester les transitions de l'Animator.
[RequireComponent(typeof(Animator))]
public class AnimTestPlayer : MonoBehaviour
{
    public float walkSpeed = 1.5f;   // seuils du blend tree "Locomotion"
    public float runSpeed = 5f;
    public float acceleration = 6f;
    public float turnSpeed = 540f;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int WaveId = Animator.StringToHash("Wave");

    static readonly string[] StateNames = { "Locomotion", "Waving" };

    Animator animator;
    float currentSpeed;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 input = ReadMoveInput();

        float maxSpeed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, input.magnitude * maxSpeed, acceleration * dt);

        if (input.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(input);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
        }
        transform.position += transform.forward * currentSpeed * dt;
        animator.SetFloat(SpeedId, currentSpeed);

        // Saluer seulement à l'arrêt, sinon le trigger reste en attente et se déclenche à l'arrêt suivant
        bool wavePressed = Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space);
        if (wavePressed && currentSpeed < 0.1f) animator.SetTrigger(WaveId);
    }

    // ZQSD (AZERTY), WASD (QWERTY) et flèches
    static Vector3 ReadMoveInput()
    {
        float x = 0f, z = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
        if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) z += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) z -= 1f;
        return Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
    }

    string CurrentStateName()
    {
        var info = animator.GetCurrentAnimatorStateInfo(0);
        foreach (var name in StateNames)
            if (info.IsName(name)) return name;
        return "?";
    }

    void OnGUI()
    {
        GUI.Box(new Rect(10f, 10f, 260f, 130f), "Perso joueur (devant)");
        GUILayout.BeginArea(new Rect(20f, 35f, 240f, 105f));
        GUILayout.Label("ZQSD / WASD / flèches : marcher");
        GUILayout.Label("Maj gauche : courir");
        GUILayout.Label("E / Espace : saluer (à l'arrêt)");
        GUILayout.Label($"Speed = {currentSpeed:0.00}   État = {CurrentStateName()}");
        GUILayout.EndArea();
    }
}
