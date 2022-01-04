using System;
using System.Collections.Generic;
using System.Text;
using RoR2;
using EntityStates;
using UnityEngine;
using RoR2.Projectile;
using EntityStates.Mage.Weapon;

namespace CircleSnapfreeze.States
{
	// To start with, with this class, I simply copy + pasted the entire class for Artificer's Snapfreeze state from dnSpy.
	// In most cases it would be more ideal to do changes via hooks, but with Circle Snap we need to change large sections of code at once.
	// I went through and modified select parts to get the state to behave how I want, but otherwise the logic is the same.
	// The changed portions have been highlighted with comments.
    class PrepCircleWall : BaseState
	{
		/*
		public static float baseDuration;
		public static GameObject areaIndicatorPrefab;
		public static float damageCoefficient;
		public static GameObject muzzleflashEffect;
		public static GameObject goodCrosshairPrefab;
		public static GameObject badCrosshairPrefab;
		public static string prepWallSoundString;
		public static float maxDistance;
		public static string fireSoundString;
		public static float maxSlopeAngle;
		*/
		//We don't need any of those public static variables, because we are referencing from vanilla Snapfreeze as much as possible.
		//The only exception is the projectile prefab, which we made one of our own in the base plugin.
		public static GameObject projectilePrefab = CircleSnapPlugin.CircleSnapWalkerPrefab;

		private bool goodPlacement;
		private GameObject areaIndicatorInstance;
		private GameObject cachedCrosshairPrefab;
		private float duration;
		private float stopwatch;

		public override void OnEnter()
		{
			base.OnEnter();
			this.duration = PrepWall.baseDuration / this.attackSpeedStat;
			base.characterBody.SetAimTimer(this.duration + 2f);
			this.cachedCrosshairPrefab = base.characterBody.crosshairPrefab;
			base.PlayAnimation("Gesture, Additive", "PrepWall", "PrepWall.playbackRate", this.duration);
			Util.PlaySound(PrepWall.prepWallSoundString, base.gameObject);
			// The only thing that needed to be changed in this method is the area indicator so that it doesnt display as a rectangle.
			// Luckily, we can easily get a circular area indicator from Huntress's Arrow Rain.
			this.areaIndicatorInstance = UnityEngine.Object.Instantiate<GameObject>(EntityStates.Huntress.ArrowRain.areaIndicatorPrefab);
			this.UpdateAreaIndicator();
		}

		private void UpdateAreaIndicator()
		{
			// All that need be changed here is to adjust the size of the area indicator to match the size of the circle.
			// I had to multiply the size by a fraction because it didn't fit quite right.
			this.areaIndicatorInstance.transform.localScale = Vector3.one * CircleSnapPlugin.CircleMaxRadius.Value * 0.6f;

			this.goodPlacement = false;
			this.areaIndicatorInstance.SetActive(true);
			if (this.areaIndicatorInstance)
			{
				float num = PrepWall.maxDistance;
				float num2 = 0f;
				Ray aimRay = base.GetAimRay();
				RaycastHit raycastHit;
				if (Physics.Raycast(CameraRigController.ModifyAimRayIfApplicable(aimRay, base.gameObject, out num2), out raycastHit, num + num2, LayerIndex.world.mask))
				{
					this.areaIndicatorInstance.transform.position = raycastHit.point;
					this.areaIndicatorInstance.transform.up = raycastHit.normal;
					this.areaIndicatorInstance.transform.forward = -aimRay.direction;
					this.goodPlacement = (Vector3.Angle(Vector3.up, raycastHit.normal) < PrepWall.maxSlopeAngle);
				}
				base.characterBody.crosshairPrefab = (this.goodPlacement ? PrepWall.goodCrosshairPrefab : PrepWall.badCrosshairPrefab);
			}
			this.areaIndicatorInstance.SetActive(this.goodPlacement);
		}

		public override void Update()
		{
			base.Update();
			this.UpdateAreaIndicator();
		}

		public override void FixedUpdate()
		{
			base.FixedUpdate();
			this.stopwatch += Time.fixedDeltaTime;
			if (this.stopwatch >= this.duration && !base.inputBank.skill3.down && base.isAuthority)
			{
				this.outer.SetNextStateToMain();
			}
		}

		public override void OnExit()
		{
			if (!this.outer.destroying)
			{
				if (this.goodPlacement)
				{
					base.PlayAnimation("Gesture, Additive", "FireWall");
					Util.PlaySound(PrepWall.fireSoundString, base.gameObject);
					if (this.areaIndicatorInstance && base.isAuthority)
					{
						EffectManager.SimpleMuzzleFlash(PrepWall.muzzleflashEffect, base.gameObject, "MuzzleLeft", true);
						EffectManager.SimpleMuzzleFlash(PrepWall.muzzleflashEffect, base.gameObject, "MuzzleRight", true);
						Vector3 forward = this.areaIndicatorInstance.transform.forward;
						forward.y = 0f;
						forward.Normalize();
						Vector3 vector = Vector3.Cross(Vector3.up, forward);
						bool crit = Util.CheckRoll(this.critStat, base.characterBody.master);

						// The commented out code here is what the original Snapfreeze used to fire it's walkers. We don't need this, obviously.
						/*
						ProjectileManager.instance.FireProjectile(PrepWall.projectilePrefab, 
						this.areaIndicatorInstance.transform.position + Vector3.up, Util.QuaternionSafeLookRotation(vector), 
						base.gameObject, this.damageStat * PrepWall.damageCoefficient, 0f, crit, DamageColorIndex.Default, null, -1f);

						ProjectileManager.instance.FireProjectile(PrepWall.projectilePrefab, 
						this.areaIndicatorInstance.transform.position + Vector3.up, Util.QuaternionSafeLookRotation(-vector), 
						base.gameObject, this.damageStat * PrepWall.damageCoefficient, 0f, crit, DamageColorIndex.Default, null, -1f);
						*/

						int totalRays = CircleSnapPlugin.TotalRays.Value;
						float angleOffset = CircleSnapPlugin.RayRotationOffset.Value;
						float angleDelta = 360f / totalRays;

						// I dont know what all the math here does, I just copied it from the worms-firing-meatballs logic. It works.
						Vector3 surfaceNormal = this.areaIndicatorInstance.transform.up;
						Vector3 normalized = Vector3.ProjectOnPlane(forward, surfaceNormal).normalized;
						Vector3 point = Vector3.RotateTowards(surfaceNormal, vector, 90 * 0.0174532924f, float.PositiveInfinity);

						// Here I set up a loop to fire as many rays as determined by config.
						// This way, the user can choose how many rays they want,
						// and we also don't have to go insane copy + pasting the same fire projectile line a dozen times for slightly different angles.
						for (int i = 0; i < totalRays; i++)
						{
							Vector3 forward2 = Quaternion.AngleAxis(angleDelta * (float)i + angleOffset, surfaceNormal) * point;

							ProjectileManager.instance.FireProjectile(PrepCircleWall.projectilePrefab,
							this.areaIndicatorInstance.transform.position + Vector3.up, Util.QuaternionSafeLookRotation(forward2),
							base.gameObject, this.damageStat * PrepWall.damageCoefficient, 0f, crit, DamageColorIndex.Default, null, -1f);
						}
					}
				}
				else
				{
					base.skillLocator.utility.AddOneStock();
					base.PlayCrossfade("Gesture, Additive", "BufferEmpty", 0.2f);
				}
			}
			EntityState.Destroy(this.areaIndicatorInstance.gameObject);
			base.characterBody.crosshairPrefab = this.cachedCrosshairPrefab;
			base.OnExit();
		}

		public override InterruptPriority GetMinimumInterruptPriority()
		{
			return InterruptPriority.Pain;
		}
	}
}
