using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Perso d'un joueur en ligne. Seul le propriétaire lit les touches ; position et animations sont synchronisées
// par ClientNetworkTransform et OwnerNetworkAnimator.
public class NetworkPlayer : NetworkBehaviour
{
    public float walkSpeed = 1.5f;
    public float runSpeed = 5f;
    public float acceleration = 6f;
    public float turnSpeed = 540f;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int WaveId = Animator.StringToHash("Wave");

    public readonly NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public readonly NetworkVariable<int> Palette = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    Animator animator;
    OwnerNetworkAnimator networkAnimator;
    float currentSpeed;
    GUIStyle nameStyle;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        networkAnimator = GetComponentInChildren<OwnerNetworkAnimator>();
    }

    public override void OnNetworkSpawn()
    {
        Palette.OnValueChanged += (_, index) => ApplyPalette(index);
        ApplyPalette(Palette.Value);

        if (!IsOwner) return;

        PlayerName.Value = new FixedString64Bytes(PlayerProfile.Name);
        Palette.Value = PlayerProfile.Palette;

        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.target = transform;
        }
    }

    void ApplyPalette(int index)
    {
        if (NetworkGameManager.Instance != null)
            PlayerProfile.ApplyPalette(gameObject, NetworkGameManager.Instance.GetPalette(index));
    }

    // ---------- Chat ----------

    public void SendChat(string message)
    {
        if (message.Length > GameChat.MaxLength) message = message.Substring(0, GameChat.MaxLength);
        SendChatServerRpc(message);
    }

    [ServerRpc]
    void SendChatServerRpc(string message)
    {
        if (message.Length > GameChat.MaxLength) message = message.Substring(0, GameChat.MaxLength);
        ReceiveChatClientRpc(message);
    }

    [ClientRpc]
    void ReceiveChatClientRpc(string message)
    {
        if (GameChat.Instance != null)
            GameChat.Instance.AddMessage(PlayerName.Value.ToString(), message);
    }

    // ---------- Déplacement ----------

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        float dt = Time.deltaTime;
        // Pendant la saisie dans le chat, les touches servent au texte
        bool typing = GameChat.IsTyping;
        Vector3 input = typing ? Vector3.zero : ReadMoveInput();

        float maxSpeed = !typing && Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, input.magnitude * maxSpeed, acceleration * dt);

        if (input.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(input);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
        }
        transform.position += transform.forward * currentSpeed * dt;
        animator.SetFloat(SpeedId, currentSpeed);

        // Saluer seulement à l'arrêt (le salut est immobile, se déplacer l'annule)
        bool wavePressed = !typing && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space));
        if (wavePressed && currentSpeed < 0.1f) networkAnimator.SetTrigger(WaveId);
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

    // Pseudo au-dessus de la tête
    void OnGUI()
    {
        var cam = Camera.main;
        if (!IsSpawned || cam == null) return;
        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.1f);
        if (p.z < 0f) return;

        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
        }
        nameStyle.normal.textColor = IsOwner ? new Color(1f, 0.85f, 0.3f) : Color.white;
        GUI.Label(new Rect(p.x - 90f, Screen.height - p.y - 12f, 180f, 24f), PlayerName.Value.ToString(), nameStyle);
    }
}
