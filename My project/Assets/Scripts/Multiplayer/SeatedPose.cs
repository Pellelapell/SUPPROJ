using UnityEngine;

// Pose assise par IK (pas d'animation "assis" dans le projet) : le bassin descend sur la chaise, les pieds restent
// au sol devant, les mains se posent sur le bureau. Sur l'objet de l'Animator (OnAnimatorIK), avec "IK Pass" activé
// sur le calque du contrôleur (CharacterSetup.CreatePlayerController).
[RequireComponent(typeof(Animator))]
public class SeatedPose : MonoBehaviour
{
    const float HipsAboveSeat = 0.1f;
    const float FeetForward = 0.45f, FeetApart = 0.12f, AnkleHeight = 0.09f;
    const float HandsOnDesk = 0.15f, HandsApart = 0.2f;

    Animator animator;
    NetworkPlayer player;

    void Awake()
    {
        animator = GetComponent<Animator>();
        player = GetComponentInParent<NetworkPlayer>();
    }

    void OnAnimatorIK(int layer)
    {
        Seat seat = player != null && player.IsSpawned ? player.CurrentSeat : null;
        bool desk = seat != null && seat.deskHeight > 0f;
        SetWeight(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, seat != null ? 1f : 0f);
        SetWeight(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, seat != null ? 1f : 0f);
        SetWeight(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, desk ? 1f : 0f);
        SetWeight(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, desk ? 1f : 0f);
        if (seat == null) return;

        Transform root = player.transform;
        Vector3 floor = root.position, forward = root.forward, right = root.right, up = Vector3.up;

        Vector3 body = animator.bodyPosition;
        body.y = floor.y + seat.seatHeight + HipsAboveSeat;
        animator.bodyPosition = body;

        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float side = left ? -1f : 1f;
            Vector3 foot = floor + forward * FeetForward + right * (side * FeetApart) + up * AnkleHeight;
            animator.SetIKPosition(left ? AvatarIKGoal.LeftFoot : AvatarIKGoal.RightFoot, foot);
            animator.SetIKRotation(left ? AvatarIKGoal.LeftFoot : AvatarIKGoal.RightFoot, root.rotation);
            animator.SetIKHintPosition(left ? AvatarIKHint.LeftKnee : AvatarIKHint.RightKnee,
                floor + forward * (FeetForward + 0.2f) + up * 0.5f + right * (side * 0.15f));

            if (!desk) continue;
            Vector3 hand = floor + forward * (seat.deskDistance + HandsOnDesk) + right * (side * HandsApart) + up * (seat.deskHeight + 0.04f);
            animator.SetIKPosition(left ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand, hand);
            animator.SetIKHintPosition(left ? AvatarIKHint.LeftElbow : AvatarIKHint.RightElbow,
                floor + forward * 0.05f + up * 0.85f + right * (side * 0.45f));
        }
    }

    void SetWeight(AvatarIKGoal goal, AvatarIKHint hint, float weight)
    {
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKHintPositionWeight(hint, weight);
        if (goal == AvatarIKGoal.LeftFoot || goal == AvatarIKGoal.RightFoot) animator.SetIKRotationWeight(goal, weight);
    }
}
