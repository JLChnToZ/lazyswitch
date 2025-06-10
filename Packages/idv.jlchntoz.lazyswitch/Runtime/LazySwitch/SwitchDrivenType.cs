using UnityEngine;

namespace JLChnToZ.VRC {
    public enum SwitchDrivenType {
        Unknown = 0,
        GameObject,
        UdonBehaviour,
        Renderer,
        Collider,
        Camera,
        Rigidbody,
        UGUISelectable,
        PositionConstraint,
        RotationConstraint,
        ScaleConstraint,
        ParentConstraint,
        AimConstraint,
        LookAtConstraint,
        VRCPickup,
        CustomRenderTexture,
        ParticleSystemEmissionModule,
        ParticleSystemShapeModule,
        ParticleSystemVelocityOverLifetimeModule,
        ParticleSystemLimitVelocityOverLifetimeModule,
        ParticleSystemInheritVelocityModule,
        ParticleSystemForceOverLifetimeModule,
        ParticleSystemColorOverLifetimeModule,
        ParticleSystemColorBySpeedModule,
        ParticleSystemSizeOverLifetimeModule,
        ParticleSystemSizeBySpeedModule,
        ParticleSystemRotationOverLifetimeModule,
        ParticleSystemRotationBySpeedModule,
        ParticleSystemExternalForcesModule,
        ParticleSystemNoiseModule,
        ParticleSystemCollisionModule,
        ParticleSystemTriggerModule,
        ParticleSystemSubEmittersModule,
        ParticleSystemTextureSheetAnimationModule,
        ParticleSystemLightsModule,
        ParticleSystemTrailModule,
        ParticleSystemCustomDataModule,
        AnimatorBool,
        AnimatorTrigger,
        // Vaild only in editor
        ParticleSystem = -1,
        Animator = -2,
    }

    public enum TriggerMode {
        InteractLocal,
        InteractGlobal,
        LocalPlayerEnter,
        LocalPlayerExit,
        AnyPlayerEnter,
        AnyPlayerExit,
        FirstPlayerEnter,
        LastPlayerExit,
        Manual,
    }

    public enum FixupMode {
        [InspectorName("Keep As-Is")]
        AsIs,
        [InspectorName("Update on World Build")]
        OnBuild,
        [InspectorName("Update on Switch Enable")]
        OnEnable,
    }
}