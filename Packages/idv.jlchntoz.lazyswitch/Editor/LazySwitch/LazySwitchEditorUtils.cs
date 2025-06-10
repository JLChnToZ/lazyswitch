using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Animations;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.VRC {
    static class LazySwitchEditorUtils {
        public static SwitchDrivenType GetTypeCode(UnityObject obj) =>
            obj is GameObject ? SwitchDrivenType.GameObject :
            obj is IUdonEventReceiver ? SwitchDrivenType.UdonBehaviour :
            obj is Renderer ? SwitchDrivenType.Renderer :
            obj is Collider ? SwitchDrivenType.Collider :
            obj is Camera ? SwitchDrivenType.Camera :
            obj is Rigidbody ? SwitchDrivenType.Rigidbody :
            obj is Selectable ? SwitchDrivenType.UGUISelectable :
            obj is PositionConstraint ? SwitchDrivenType.PositionConstraint :
            obj is RotationConstraint ? SwitchDrivenType.RotationConstraint :
            obj is ScaleConstraint ? SwitchDrivenType.ScaleConstraint :
            obj is ParentConstraint ? SwitchDrivenType.ParentConstraint :
            obj is AimConstraint ? SwitchDrivenType.AimConstraint :
            obj is LookAtConstraint ? SwitchDrivenType.LookAtConstraint :
            obj is VRC_Pickup ? SwitchDrivenType.VRCPickup :
            obj is CustomRenderTexture ? SwitchDrivenType.CustomRenderTexture :
            obj is ParticleSystem ? SwitchDrivenType.ParticleSystem :
            obj is Animator ? SwitchDrivenType.Animator :
            SwitchDrivenType.Unknown;

        public static bool IsActive(UnityObject obj, SwitchDrivenType subType, string parameter, bool isCurrentState = false) => obj != null && (
            obj is GameObject gameObject ? gameObject.activeSelf :
            obj is IConstraint constraint ? constraint.constraintActive :
            obj is Selectable selectable ? selectable.interactable :
            obj is VRC_Pickup pickup ? pickup.pickupable :
            obj is Rigidbody rigidbody ? !rigidbody.isKinematic :
            obj is Animator animator ? subType switch {
                SwitchDrivenType.AnimatorBool => GetAnimatorBool(animator, parameter),
                SwitchDrivenType.AnimatorTrigger => isCurrentState,
                _ => true,
            } :
            obj is Behaviour behaviour ? behaviour.enabled :
            obj is CustomRenderTexture crt ? crt.updateMode == CustomRenderTextureUpdateMode.Realtime :
            obj is ParticleSystem ps ? subType switch {
                SwitchDrivenType.ParticleSystemEmissionModule => ps.emission.enabled,
                SwitchDrivenType.ParticleSystemShapeModule => ps.shape.enabled,
                SwitchDrivenType.ParticleSystemVelocityOverLifetimeModule => ps.velocityOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemLimitVelocityOverLifetimeModule => ps.limitVelocityOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemInheritVelocityModule => ps.inheritVelocity.enabled,
                SwitchDrivenType.ParticleSystemForceOverLifetimeModule => ps.forceOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemColorOverLifetimeModule => ps.colorOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemColorBySpeedModule => ps.colorBySpeed.enabled,
                SwitchDrivenType.ParticleSystemSizeOverLifetimeModule => ps.sizeOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemSizeBySpeedModule => ps.sizeBySpeed.enabled,
                SwitchDrivenType.ParticleSystemRotationOverLifetimeModule => ps.rotationOverLifetime.enabled,
                SwitchDrivenType.ParticleSystemRotationBySpeedModule => ps.rotationBySpeed.enabled,
                SwitchDrivenType.ParticleSystemExternalForcesModule => ps.externalForces.enabled,
                SwitchDrivenType.ParticleSystemNoiseModule => ps.noise.enabled,
                SwitchDrivenType.ParticleSystemCollisionModule => ps.collision.enabled,
                SwitchDrivenType.ParticleSystemTriggerModule => ps.trigger.enabled,
                SwitchDrivenType.ParticleSystemSubEmittersModule => ps.subEmitters.enabled,
                SwitchDrivenType.ParticleSystemTextureSheetAnimationModule => ps.textureSheetAnimation.enabled,
                SwitchDrivenType.ParticleSystemLightsModule => ps.lights.enabled,
                SwitchDrivenType.ParticleSystemTrailModule => ps.trails.enabled,
                SwitchDrivenType.ParticleSystemCustomDataModule => ps.customData.enabled,
                _ => true
            } :
            true
        );

        static bool GetAnimatorBool(Animator animator, string parameter) {
            var parameters = animator.parameters;
            foreach (var param in parameters)
                if (param.name == parameter &&
                    param.type == AnimatorControllerParameterType.Bool)
                    return param.defaultBool;
            return false;
        }

        public static void ToggleActive(UnityObject obj, SwitchDrivenType subType) {
            if (obj == null) return;
            if (obj is GameObject gameObject) gameObject.SetActive(!gameObject.activeSelf);
            else if (obj is IConstraint constraint) constraint.constraintActive = !constraint.constraintActive;
            else if (obj is Selectable selectable) selectable.interactable = !selectable.interactable;
            else if (obj is VRC_Pickup pickup) pickup.pickupable = !pickup.pickupable;
            else if (obj is Rigidbody rigidbody) rigidbody.isKinematic = !rigidbody.isKinematic;
            else if (obj is Animator) return;
            else if (obj is Behaviour behaviour) behaviour.enabled = !behaviour.enabled;
            else if (obj is CustomRenderTexture crt)
                crt.updateMode = crt.updateMode == CustomRenderTextureUpdateMode.Realtime ?
                    CustomRenderTextureUpdateMode.OnDemand : CustomRenderTextureUpdateMode.Realtime;
            else if (obj is ParticleSystem ps) switch (subType) {
                    case SwitchDrivenType.ParticleSystemEmissionModule: { var m = ps.emission; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemShapeModule: { var m = ps.shape; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemVelocityOverLifetimeModule: { var m = ps.velocityOverLifetime; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemLimitVelocityOverLifetimeModule: { var m = ps.limitVelocityOverLifetime; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemInheritVelocityModule: { var m = ps.inheritVelocity; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemForceOverLifetimeModule: { var m = ps.forceOverLifetime; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemColorOverLifetimeModule: { var m = ps.colorOverLifetime; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemColorBySpeedModule: { var m = ps.colorBySpeed; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemSizeOverLifetimeModule: { var m = ps.sizeOverLifetime; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemSizeBySpeedModule: { var m = ps.sizeBySpeed; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemRotationOverLifetimeModule: { var m = ps.rotationOverLifetime; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemRotationBySpeedModule: { var m = ps.rotationBySpeed; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemExternalForcesModule: { var m = ps.externalForces; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemNoiseModule: { var m = ps.noise; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemCollisionModule: { var m = ps.collision; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemTriggerModule: { var m = ps.trigger; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemSubEmittersModule: { var m = ps.subEmitters; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemTextureSheetAnimationModule: { var m = ps.textureSheetAnimation; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemLightsModule: { var m = ps.lights; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemTrailModule: { var m = ps.trails; m.enabled = !m.enabled; } break;
                    case SwitchDrivenType.ParticleSystemCustomDataModule: { var m = ps.customData; m.enabled = !m.enabled; } break;
                }
        }
    }
}