using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Audio;
using BepInEx.Configuration;
using System.Linq;
using Unity.Netcode.Components;
using Unity.Netcode.Samples;
using UnityEngine.AI;

namespace LCPeeper
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class Peeper : BaseUnityPlugin
    {
        private const string modGUID = "x753.Peepers";
        private const string modName = "Peepers";
        private const string modVersion = "0.9.2";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static Peeper Instance;

		public static GameObject PeeperPrefab;
		public static EnemyType PeeperType;
		public static TerminalNode PeeperFile;

		public static int PeeperCreatureID = 0;

		public static List<PeeperAI> PeeperList = new List<PeeperAI>();

		public static float PeeperSpawnChance;
		public static int PeeperMinGroupSize;
		public static int PeeperMaxGroupSize;

        private void Awake()
        {
			AssetBundle peeperAssetBundle = AssetBundle.LoadFromMemory(LCPeeper.Properties.Resources.peeper);
			PeeperPrefab = peeperAssetBundle.LoadAsset<GameObject>("Assets/PeeperPrefab.prefab");
			PeeperType = peeperAssetBundle.LoadAsset<EnemyType>("Assets/PeeperType.asset");
			PeeperFile = peeperAssetBundle.LoadAsset<TerminalNode>("Assets/PeeperFile.asset");

			if (Instance == null)
            {
                Instance = this;
            }

            harmony.PatchAll();
            Logger.LogInfo($"Plugin {modName} is loaded!");

			// Handle configs
			{
				PeeperSpawnChance = Config.Bind("Spawn Rate", "Hourly Spawn Chance (%)", 10f, "Chance of a group of peepers spawning each hour").Value;
				PeeperMinGroupSize = Config.Bind("Spawn Rate", "Minimum Peeper Group Size", 2, "The minimum number of peepers in a single group").Value;
				PeeperMaxGroupSize = Config.Bind("Spawn Rate", "Maximum Peeper Group Size", 4, "The maximum number of peepers in a single group").Value;
			}

			// UnityNetcodeWeaver patch requires this
			var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

		[HarmonyPatch(typeof(GameNetworkManager))]
		internal class GameNetworkManagerPatch
		{
			[HarmonyPatch("Start")]
			[HarmonyPostfix]
			static void StartPatch()
			{
				GameNetworkManager.Instance.GetComponent<NetworkManager>().AddNetworkPrefab(PeeperPrefab); // Register the network prefab
			}
		}
	}

	[HarmonyPatch(typeof(Terminal))]
	internal class TerminalPatch
	{
		[HarmonyPatch("Start")]
		[HarmonyPostfix]
		static void StartPatch(Terminal __instance)
		{
			Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
			if (!terminal.enemyFiles.Find(node => node.creatureName == "Peepers"))
			{
				// Add peepers to the bestiary
				Peeper.PeeperCreatureID = terminal.enemyFiles.Count;
				Peeper.PeeperFile.creatureFileID = Peeper.PeeperCreatureID;
				terminal.enemyFiles.Add(Peeper.PeeperFile);

				TerminalKeyword infoKeyword = terminal.terminalNodes.allKeywords.First(keyword => keyword.word == "info");

				TerminalKeyword peeperKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
				peeperKeyword.word = "peepers";
				peeperKeyword.isVerb = false;
				peeperKeyword.defaultVerb = infoKeyword;

				List<CompatibleNoun> itemInfoNouns = infoKeyword.compatibleNouns.ToList();
				itemInfoNouns.Add(new CompatibleNoun()
				{
					noun = peeperKeyword,
					result = Peeper.PeeperFile
				});
				infoKeyword.compatibleNouns = itemInfoNouns.ToArray();

				List<TerminalKeyword> allKeywords = terminal.terminalNodes.allKeywords.ToList();
				allKeywords.Add(peeperKeyword);
				terminal.terminalNodes.allKeywords = allKeywords.ToArray();
			}
		}
	}

	[HarmonyPatch(typeof(RoundManager))]
	internal class RoundManagerPatch
	{
		// The base game's spawning system doesn't really work well for something like this, so we'll do it our own way
		[HarmonyPatch("AdvanceHourAndSpawnNewBatchOfEnemies")]
		[HarmonyPrefix]
		static void AdvanceHourAndSpawnNewBatchOfEnemiesPatch(RoundManager __instance)
		{
			float spawnRoll = UnityEngine.Random.Range(0f, 100f);
			if (spawnRoll > Peeper.PeeperSpawnChance) { return; }

			GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("OutsideAINode");
			Vector3 spawnPoint = spawnPoints[__instance.AnomalyRandom.Next(0, spawnPoints.Length)].transform.position;
			spawnPoint = __instance.GetRandomNavMeshPositionInRadius(spawnPoint, 4f, default(NavMeshHit));

			int spawnIndex = 0;
			bool flag = false;
			for (int j = 0; j < spawnPoints.Length - 1; j++)
			{
				for (int k = 0; k < __instance.spawnDenialPoints.Length; k++)
				{
					flag = true;
					if (Vector3.Distance(spawnPoint, __instance.spawnDenialPoints[k].transform.position) < 8f)
					{
						spawnIndex = (spawnIndex + 1) % spawnPoints.Length;
						spawnPoint = spawnPoints[spawnIndex].transform.position;
						spawnPoint = __instance.GetRandomNavMeshPositionInRadius(spawnPoint, 4f, default(NavMeshHit));
						flag = false;
						break;
					}
				}
				if (flag)
				{
					break;
				}
			}

			int peeperGroupSize = UnityEngine.Random.Range(Peeper.PeeperMinGroupSize, Peeper.PeeperMaxGroupSize);
			for (int i = 0; i <= peeperGroupSize; i++)
			{
				RoundManager.Instance.SpawnEnemyGameObject(spawnPoint, 0f, -1, Peeper.PeeperType);
				Peeper.PeeperType.numberSpawned++;
				RoundManager.Instance.currentDaytimeEnemyPower += 1;
			}
		}
	}

	[HarmonyPatch(typeof(StartOfRound))]
	internal class StartOfRoundPatch
	{
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        static void AwakePatch(ref StartOfRound __instance)
        {
            foreach (SelectableLevel level in __instance.levels)
            {
                if (!level.Enemies.Any(e => e.enemyType == Peeper.PeeperType))
                {
                    int rarity = 0;
                    level.DaytimeEnemies.Add(new SpawnableEnemyWithRarity()
                    {
                        enemyType = Peeper.PeeperType,
                        rarity = rarity,
                    });
					return; // We're only adding peepers to this list for other mods that use this list
                }
            }
        }

        [HarmonyPatch("EndOfGame")]
		[HarmonyPrefix]
		static void EndOfGamePatch(StartOfRound __instance, int bodiesInsured = 0, int connectedPlayersOnServer = 0, int scrapCollected = 0)
		{
			foreach (PeeperAI peeper in Peeper.PeeperList)
			{
				if (peeper.isAttached && peeper.attachedPlayer != null)
				{
					if (!peeper.attachedPlayer.isPlayerDead)
					{
						peeper.IsWeighted = false;
					}
				}
			}
			Peeper.PeeperList = new List<PeeperAI>();
			PeeperAI.UsedAttachTargets = new List<Transform>();
		}
	}

	[HarmonyPatch(typeof(StunGrenadeItem))]
	internal class StunGrenadeItemPatch
	{
		[HarmonyPatch("StunExplosion")]
		[HarmonyPostfix]
		static void StunExplosion(StunGrenadeItem __instance, Vector3 explosionPosition, bool affectAudio, float flashSeverityMultiplier, float enemyStunTime, float flashSeverityDistanceRolloff = 1f, bool isHeldItem = false, PlayerControllerB playerHeldBy = null, PlayerControllerB playerThrownBy = null, float addToFlashSeverity = 0f)
		{
			PlayerControllerB playerControllerB = GameNetworkManager.Instance.localPlayerController;

			if (Vector3.Distance(playerControllerB.transform.position, explosionPosition) < 16f)
			{
				if (Vector3.Distance(playerControllerB.transform.position, explosionPosition) > 5f)
				{
					if (!(isHeldItem && playerHeldBy == GameNetworkManager.Instance.localPlayerController))
					{
						if (Physics.Linecast(explosionPosition + Vector3.up * 0.5f, playerControllerB.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
						{
							return;
						}
					}
				}

				// Make every peeper die if the player gets flashed
				List<PeeperAI> allPeepers = new List<PeeperAI>(Peeper.PeeperList);
				foreach (PeeperAI peeper in allPeepers)
				{
					if (peeper.attachedPlayer == playerControllerB)
					{
						if (peeper.IsOwner)
						{
							peeper.KillEnemyOnOwnerClient();
						}
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(SprayPaintItem))]
	internal class SprayPaintItemPatch
	{
		static FieldInfo SprayMatIndex = typeof(SprayPaintItem).GetField("sprayCanMatsIndex", BindingFlags.NonPublic | BindingFlags.Instance);

		[HarmonyPatch("SprayPaintClientRpc")]
		[HarmonyPrefix]
		static void SprayPaintClientRpcPatch(SprayPaintItem __instance, Vector3 sprayPos, Vector3 sprayRot)
		{
			try
			{
				RaycastHit raycastHit;
				Ray ray = new Ray(sprayPos, sprayRot);
				if (Physics.Raycast(ray, out raycastHit, 4f, (1 << 19), QueryTriggerInteraction.Collide))
				{
					if (raycastHit.collider != null && raycastHit.collider.name == "PeeperSprayCollider")
					{
						PeeperAI peeper = raycastHit.collider.transform.parent.parent.parent.parent.parent.parent.parent.GetComponent<PeeperAI>();

						if (__instance.playerHeldBy == peeper.attachedPlayer) { return; }

						//if (peeper.peeperMesh.materials.Length < 2)
						//{
						//    if (peeper.isEnemyDead)
						//    {
						//        peeper.peeperMesh.materials = new Material[] { peeper.paintedMat, peeper.deadMat };
						//    }
						//    else
						//    {
						//        peeper.peeperMesh.materials = new Material[] { peeper.paintedMat, peeper.baseMat };

						//    }
						//}
						peeper.peeperMesh.materials[0].color = __instance.sprayCanMats[(int)SprayMatIndex.GetValue(__instance)].color;

						if (peeper.isAttached)
						{
							peeper.EjectFromPlayerServerRpc(peeper.attachedPlayer.actualClientId);
						}
					}
				}
			}
			catch (Exception e)
			{
			}
		}
	}

	// Make the laser pointer kill a peeper when you shine it in its eye
	[HarmonyPatch(typeof(FlashlightItem))]
	internal class FlashlightItemPatch
	{
		[HarmonyPatch("Update")]
		[HarmonyPostfix]
		static void UpdatePatch(FlashlightItem __instance)
		{
			if (__instance.IsOwner && __instance.flashlightTypeID == 2 && __instance.isBeingUsed)
			{
				Transform laser = __instance.flashlightBulb.transform;
				Ray ray = new Ray(laser.position, laser.forward);
				RaycastHit[] hits = Physics.RaycastAll(ray, 32f, 1 << LayerMask.NameToLayer("ScanNode"));

				foreach (RaycastHit hit in hits)
				{
					if (hit.collider.name == "PeeperScanNode")
					{
						float angle = Vector3.Angle(-ray.direction, hit.transform.forward);
						if (angle < 55f)
						{
							PeeperAI peeper = hit.transform.parent.parent.parent.parent.parent.parent.parent.GetComponent<PeeperAI>();

							if (peeper.attachedPlayer == GameNetworkManager.Instance.localPlayerController)
							{
								return;
							}

							if (peeper.IsOwner)
							{
								peeper.KillEnemyOnOwnerClient();
							}
							else
							{
								peeper.KillEnemyNetworked();
							}
						}
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(PlayerControllerB))]
	internal class PlayerControllerBPatch
	{
		[HarmonyPatch("KillPlayer")]
		[HarmonyPrefix]
		static void KillPlayerPatch(PlayerControllerB __instance, Vector3 bodyVelocity, bool spawnBody = true, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown, int deathAnimation = 0)
		{
			// Make every peeper fly off the player if the player dies
			List<PeeperAI> allPeepers = new List<PeeperAI>(Peeper.PeeperList);
			foreach (PeeperAI peeper in allPeepers)
			{
				if (peeper.attachedPlayer == __instance)
				{
					if (peeper.IsOwner)
					{
						if (causeOfDeath == CauseOfDeath.Electrocution || causeOfDeath == CauseOfDeath.Blast)
						{
							peeper.KillEnemyOnOwnerClient();
						}
						else
						{
							peeper.EjectFromPlayerServerRpc(__instance.playerClientId);
						}
					}
				}
			}
		}
		[HarmonyPatch("DamagePlayer")]
		[HarmonyPrefix]
		static void DamagePlayerPatch(PlayerControllerB __instance, int damageNumber, bool hasDamageSFX = true, bool callRPC = true, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown, int deathAnimation = 0, bool fallDamage = false, Vector3 force = default(Vector3))
		{
			// Make every peeper fly off the player if the player gets injured
			List<PeeperAI> allPeepers = new List<PeeperAI>(Peeper.PeeperList);
			foreach (PeeperAI peeper in allPeepers)
			{
				if (peeper.attachedPlayer == __instance)
				{
					peeper.EjectFromPlayerServerRpc(__instance.playerClientId);
				}
			}
		}
        [HarmonyPatch("IShockableWithGun.ShockWithGun")]
        [HarmonyPrefix]
        static void DamagePlayerPatch(PlayerControllerB __instance, PlayerControllerB shockedByPlayer)
        {
			// Make every peeper die if the player gets shocked
			List<PeeperAI> allPeepers = new List<PeeperAI>(Peeper.PeeperList);
			foreach (PeeperAI peeper in allPeepers)
            {
                if (peeper.attachedPlayer == __instance)
                {
                    if (peeper.IsOwner)
                    {
                        peeper.KillEnemyOnOwnerClient();
                    }
                }
            }
        }
    }

	public class PeeperAI : EnemyAI
	{
		[Header("General")]
		public GameObject creatureModel; // The root of the creature model
		public SkinnedMeshRenderer peeperMesh;
		public ScanNodeProperties scanNode;

		[Header("AI and Pathfinding")]

		public AISearchRoutine searchForPlayers;

		public float maxSearchAndRoamRadius = 100f;
		public float searchPrecisionValue = 5f;

		private int sightRange = 30;

		[Header("Constraints and Transforms")]
		public Transform attachTargetTransform; // Current target transform for the attachConstraint
		public static List<Transform> UsedAttachTargets = new List<Transform>();

		public Vector3 attachTargetTranslationOffset;
		public Vector3 attachTargetRotationOffset;

		public Transform eyeTransform;
		public Transform eyeOriginalTransform;

		public bool isAttached;
		public PlayerControllerB attachedPlayer;

		[Header("Colliders and Physics")]

		public SphereCollider attachCollider; // Collider with a PeeperAttachHitbox script
		public SphereCollider physicsCollider; // Collider when the peeper turns into a ball and obeys the laws of physics
		public SphereCollider hitboxCollider; // Collider that is used for damaging / shocking / killing the peeper

		public Rigidbody physicsRigidbody; // Rigidbody that obeys the laws of physics

		[Header("State Handling and Sync")]
		public float stateTimer = 0f;
		public float stateTimer2 = 0f;
		public int stateCounter = 0;

		[Header("Audio")]
		public AudioSource AttachSFXSource;

		public AudioClip walkSFX;
		public AudioClip runSFX;
		public AudioClip[] spotSFX;
		public AudioClip[] jumpSFX;
		public AudioClip[] attachSFX;
		public AudioClip[] deathSFX;
		public AudioClip[] ejectSFX;
		public AudioClip[] zapSFX;

		[Header("Materials")]
		public Material baseMat;
		public Material paintedMat;
		public Material deadMat;

		public ParticleSystem deathParticles;

		[Header("Ragdoll")]
		private bool ragdollFrozen = false;
		public Rigidbody[] ragdollRigidbodies;
		public Collider[] ragdollColliders;

		public override void Start()
		{
			base.Start();
			this.searchForPlayers.searchWidth = this.maxSearchAndRoamRadius;
			this.searchForPlayers.searchPrecision = this.searchPrecisionValue;

			Peeper.PeeperList.Add(this);

            AudioMixer audioMixer = SoundManager.Instance.diageticMixer;
            base.creatureVoice.outputAudioMixerGroup = audioMixer.FindMatchingGroups("SFX")[0];
            base.creatureSFX.outputAudioMixerGroup = audioMixer.FindMatchingGroups("SFX")[0];
            this.AttachSFXSource.outputAudioMixerGroup = audioMixer.FindMatchingGroups("SFX")[0];

			this.scanNode.creatureScanID = Peeper.PeeperCreatureID;

			SwitchBehaviourState(2); // spawn state
		}

		public override void DoAIInterval()
		{
			if (base.inSpecialAnimation) { return; }
			base.DoAIInterval();

			if (StartOfRound.Instance.livingPlayers == 0 || this.isEnemyDead)
			{
				return;
			}

			int currentBehaviourStateIndex = this.currentBehaviourStateIndex;
			if (currentBehaviourStateIndex != 0)
			{
				if (this.searchForPlayers.inProgress)
				{
					base.StopSearch(this.searchForPlayers, true);
					this.movingTowardsTargetPlayer = true;
				}
			}
			else if (!this.searchForPlayers.inProgress)
			{
				base.StartSearch(base.transform.position, this.searchForPlayers);
				return;
			}
		}

		public override void FinishedCurrentSearchRoutine()
		{
			base.FinishedCurrentSearchRoutine();
			this.searchForPlayers.searchWidth = Mathf.Clamp(this.searchForPlayers.searchWidth + 20f, 1f, this.maxSearchAndRoamRadius);
		}

		private Vector3 agentLocalVelocity;
		private Vector3 previousPosition;
		private float velX;
		private float velZ;
		private float velXZ;
		private float footstepTimer = 0f;
		private void CalculateAnimationDirection()
		{
			this.agentLocalVelocity = this.transform.InverseTransformDirection(Vector3.ClampMagnitude(base.transform.position - this.previousPosition, 1f) / (Time.deltaTime * 2f));
			this.velX = Mathf.Lerp(this.velX, this.agentLocalVelocity.x, 5f * Time.deltaTime);
			this.velZ = Mathf.Lerp(this.velZ, -this.agentLocalVelocity.z, 5f * Time.deltaTime);
			this.previousPosition = base.transform.position;

			this.velXZ = Mathf.Sqrt(this.velX * this.velX + this.velZ * this.velZ);
			creatureAnimator.SetFloat("Speed", this.velXZ);

			float footstepInterval;
			if (base.currentBehaviourStateIndex == 1 || base.currentBehaviourStateIndex == 5)
			{
				footstepInterval = 0.3f;
			}
			else
			{
				footstepInterval = 0.3335f;
			}

			this.footstepTimer += Time.deltaTime;
			if (this.footstepTimer < footstepInterval) { return; }
			this.footstepTimer = 0f;

			if (this.velXZ > 0.15)
			{
				if (base.currentBehaviourStateIndex == 1)
				{
					PlayCreatureSFX(2);
				}
				else
				{
					PlayCreatureSFX(1);
				}
			}
		}
		
		private void PutCreatureOnGround()
		{
			if (base.agent.velocity.y > 5f) { return; }

			creatureAnimator.SetBool("Grounded", true);
			creatureAnimator.SetBool("Jump", false);

			Ray ray = new Ray(this.transform.position + Vector3.up, Vector3.down);
			RaycastHit hit;

			if (Physics.Raycast(ray, out hit, 2f, LayerMask.GetMask("Room", "Colliders"), QueryTriggerInteraction.Ignore))
			{
				this.creatureModel.transform.localPosition = new Vector3(0f, -hit.distance + 1f, 0f);
			}
		}

		private Vector3 deathDirection = Vector3.zero;
		public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false)
		{
			base.HitEnemy(force, playerWhoHit, false);
			this.enemyHP -= force;

			if (playerWhoHit != null)
			{
				deathDirection = (this.transform.position - playerWhoHit.transform.position).normalized;
			}

			if (this.enemyHP <= 0 && base.IsOwner)
			{
				base.KillEnemyOnOwnerClient(false);
			}
		}

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB setStunnedByPlayer = null)
        {
			this.enemyHP -= 1;
			if (this.enemyHP <= 0 && base.IsOwner)
			{
				base.KillEnemyOnOwnerClient(false);
            }
        }

		public void KillEnemyNetworked()
		{
			if (this.IsServer)
			{
				KillEnemyClientRpc();
			}
			else
			{
				KillEnemyServerRpc();
			}
		}

		[ServerRpc(RequireOwnership = false)]
		public void KillEnemyServerRpc()
		{
			KillEnemyClientRpc();
		}

		[ClientRpc]
		public void KillEnemyClientRpc()
		{
			KillEnemy();
		}

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy(false);
            base.creatureVoice.Stop();
            base.creatureSFX.Stop();

            PlayCreatureSFX(5);

            SwitchBehaviourState(7);

			base.isEnemyDead = true;

			Peeper.PeeperList.Remove(this);

            // Remove offset so the corpse doesn't fall through the floor
            this.creatureModel.transform.localPosition = Vector3.zero;

			if (this.isAttached && this.attachedPlayer != null)
			{
				EjectFromPlayer(this.attachedPlayer);
			}

			Color paintedColor = this.peeperMesh.material.color;
			this.peeperMesh.materials = new Material[] { this.deadMat };
			this.peeperMesh.materials[0].color = paintedColor;
			deathParticles.Play();

			base.creatureAnimator.enabled = false;
			this.physicsRigidbody.isKinematic = true;
			foreach (Rigidbody rb in this.ragdollRigidbodies)
			{
				rb.isKinematic = false;
				if (deathDirection != Vector3.zero)
				{
					rb.AddForce(15f * deathDirection, ForceMode.Impulse);
				}
			}
			foreach (Collider c in this.ragdollColliders)
			{
				c.enabled = true;
			}

			this.peeperMesh.SetBlendShapeWeight(0, 0);
			this.peeperMesh.SetBlendShapeWeight(1, 350);
		}

        public void SwitchBehaviourState(int state)
		{
			SwitchBehaviourStateLocally(state);
			SwitchBehaviourStateServerRpc(state);
		}

		[ServerRpc(RequireOwnership = false)]
		public void SwitchBehaviourStateServerRpc(int state)
		{
			SwitchBehaviourStateClientRpc(state);
		}

		[ClientRpc]
		public void SwitchBehaviourStateClientRpc(int state)
		{
			SwitchBehaviourStateLocally(state);
		}

		public void SwitchBehaviourStateLocally(int state)
		{
			this.stateTimer = 0f;
			this.stateTimer2 = 0f;
			this.stateCounter = 0;

			switch (state)
			{
				case 0: // Neutral State
					
					base.targetPlayer = null;
					base.movingTowardsTargetPlayer = false;

					base.agent.speed = 2f;

					
					base.creatureAnimator.SetBool("Jump", false);
					base.creatureAnimator.SetBool("Attached", false);
					base.creatureAnimator.SetBool("Dead", false);

					this.inSpecialAnimation = false;

					this.agent.enabled = true;
					this.enemyType.canBeStunned = true;

					this.attachCollider.enabled = true;
					this.physicsCollider.enabled = false;
					this.physicsRigidbody.isKinematic = true;
					this.hitboxCollider.enabled = true;

					this.hitboxCollider.radius = 0.5f;
					this.hitboxCollider.center = new Vector3(0f, 0.5f, 0f);
					break;
				case 1: // Chasing State
					
					base.agent.speed = 6f;

					
					base.creatureAnimator.SetBool("Jump", false);
					base.creatureAnimator.SetBool("Attached", false);
					base.creatureAnimator.SetBool("Dead", false);

					this.inSpecialAnimation = false;

					this.agent.enabled = true;
					this.enemyType.canBeStunned = true;

					this.attachCollider.enabled = true;
					this.physicsCollider.enabled = false;
					this.physicsRigidbody.isKinematic = true;
					this.hitboxCollider.enabled = true;

					this.hitboxCollider.radius = 0.5f;
					this.hitboxCollider.center = new Vector3(0f, 0.5f, 0f);
					break;
				case 2: // Spawning State
					
					base.targetPlayer = null;
					base.movingTowardsTargetPlayer = false;

					base.agent.speed = 0f;
					
					
					base.creatureAnimator.SetBool("Jump", false);
					base.creatureAnimator.SetBool("Attached", false);
					base.creatureAnimator.SetBool("Dead", false);
					base.creatureAnimator.SetBool("Grounded", true);

					this.inSpecialAnimation = false;

					this.agent.enabled = true;
					this.enemyType.canBeStunned = true;

					this.attachCollider.enabled = true;
					this.physicsCollider.enabled = false;
					this.physicsRigidbody.isKinematic = true;
					this.hitboxCollider.enabled = true;

					this.hitboxCollider.radius = 0.5f;
					this.hitboxCollider.center = new Vector3(0f, 0.5f, 0f);
					break;
				case 3: // Jumping State
					
					base.agent.speed = 0f;

					this.creatureModel.transform.localPosition = Vector3.zero;
					
					base.creatureAnimator.SetBool("Jump", true);
					base.creatureAnimator.SetBool("Attached", false);
					base.creatureAnimator.SetBool("Dead", false);
					base.creatureAnimator.SetBool("Grounded", false);

					this.inSpecialAnimation = true;

					this.agent.enabled = false;
					this.enemyType.canBeStunned = true;

					this.attachCollider.enabled = true;
					this.physicsCollider.enabled = true;
					this.physicsRigidbody.isKinematic = false;
					this.hitboxCollider.enabled = true;

					this.hitboxCollider.radius = 2f;
					this.hitboxCollider.center = Vector3.zero;

					this.physicsCollider.includeLayers &= ~(1 << 19); // remove Enemies layer from inclusions
					this.physicsCollider.excludeLayers |= 1 << 19; // add Enemies layer to exclusions
					break;
				case 4: // Attached State
					
					base.targetPlayer = null;
					base.movingTowardsTargetPlayer = false;

					this.creatureModel.transform.localPosition = Vector3.zero;

					base.agent.speed = 0f;

					base.creatureAnimator.SetBool("Jump", true);
					base.creatureAnimator.SetBool("Attached", true);
					base.creatureAnimator.SetBool("Dead", false);
					base.creatureAnimator.SetBool("Grounded", false);

					this.inSpecialAnimation = true;

					this.agent.enabled = false;
					this.enemyType.canBeStunned = false;

					this.attachCollider.enabled = false;
					this.physicsCollider.enabled = false;
					this.physicsRigidbody.isKinematic = true;
					this.hitboxCollider.enabled = false;

					this.hitboxCollider.radius = 0.5f;
					this.hitboxCollider.center = Vector3.zero;
					break;
				case 5: // Recovery State
					
					base.targetPlayer = null;
					base.movingTowardsTargetPlayer = false;

					base.agent.speed = 0f;
					
					
					base.creatureAnimator.SetBool("Jump", false);
					base.creatureAnimator.SetBool("Attached", false);
					base.creatureAnimator.SetBool("Dead", false);
					base.creatureAnimator.SetBool("Grounded", true);

					this.inSpecialAnimation = false;

					this.agent.enabled = true;
					this.enemyType.canBeStunned = true;

					this.attachCollider.enabled = true;
					this.physicsCollider.enabled = false;
					this.physicsRigidbody.isKinematic = true;
					this.hitboxCollider.enabled = true;

					this.hitboxCollider.radius = 0.5f;
					this.hitboxCollider.center = new Vector3(0f, 0.5f, 0f);
					break;
				case 6: // Fall Off State

					this.stateTimer = UnityEngine.Random.Range(0f, 1f); // make them all get up at slightly different times

					base.targetPlayer = null;
					base.movingTowardsTargetPlayer = false;

					base.agent.speed = 0f;

					this.creatureModel.transform.localPosition = Vector3.zero;
					
					base.creatureAnimator.SetBool("Jump", true);
					base.creatureAnimator.SetBool("Attached", false);
					base.creatureAnimator.SetBool("Dead", false);
					base.creatureAnimator.SetBool("Grounded", false);

					this.inSpecialAnimation = true;

					this.agent.enabled = false;
					this.enemyType.canBeStunned = true;

					this.attachCollider.enabled = false;
					this.physicsCollider.enabled = true;
					this.physicsRigidbody.isKinematic = false;
					this.hitboxCollider.enabled = true;

                    this.hitboxCollider.radius = 0.5f;
					this.hitboxCollider.center = Vector3.zero;

					this.physicsCollider.includeLayers |= 1 << 19; // add Enemies layer to inclusions
					this.physicsCollider.excludeLayers &= ~(1 << 19); // remove Enemies layer from exclusions
					break;
				case 7: // Dead State
					
					base.targetPlayer = null;
					base.movingTowardsTargetPlayer = false;

					base.agent.speed = 0f;

					this.creatureModel.transform.localPosition = Vector3.zero;

					base.creatureAnimator.SetBool("Jump", false);
					base.creatureAnimator.SetBool("Attached", false);
					base.creatureAnimator.SetBool("Dead", true);
					base.creatureAnimator.SetBool("Grounded", false);

					this.inSpecialAnimation = true;

					this.agent.enabled = false;
					this.enemyType.canBeStunned = false;

					this.attachCollider.enabled = false;
					this.physicsCollider.enabled = false;
					this.physicsRigidbody.isKinematic = true;
					this.hitboxCollider.enabled = false;

					this.hitboxCollider.radius = 0.5f;
					this.hitboxCollider.center = Vector3.zero;
					break;
				default:
					break;
			}

			base.SwitchToBehaviourStateOnLocalClient(state);
		}

		private float blinkTimer = 0f;
		private float blinkInterval = 0f;
		private void LateUpdate()
		{
			if (this.isEnemyDead)
			{
				return;
			}

			if (this.isAttached)
			{
				this.transform.position = this.attachTargetTransform.position;
				this.transform.rotation = this.attachTargetTransform.rotation;
				this.transform.Rotate(this.attachTargetRotationOffset);
				this.transform.Translate(0f, 0f, this.attachTargetTranslationOffset.z, Space.Self);
				this.transform.Translate(0f, this.attachTargetTranslationOffset.y, 0f, this.attachTargetTransform);
			}

			int state = base.currentBehaviourStateIndex;
			if (state == 4)
			{
				blinkTimer += Time.deltaTime;
				if (blinkTimer > blinkInterval)
				{
					this.creatureAnimator.SetTrigger("Blink");
					blinkInterval = UnityEngine.Random.Range(2f, 12f);
					blinkTimer = 0f;
				}
			}

			if (!base.isEnemyDead)
			{
				// Handle eye looking at nearby players
				if (this.attachedPlayer == GameNetworkManager.Instance.localPlayerController) { return; }

				PlayerControllerB closestPlayer = null;
				float closestDistance = 7f;
				foreach (PlayerControllerB p in StartOfRound.Instance.allPlayerScripts)
				{
					if (p == this.attachedPlayer) { continue; }
					float d = Vector3.Distance(this.eyeTransform.position, p.gameplayCamera.transform.position);

					Vector3 direction = p.transform.position - this.eyeTransform.position;
					float angle = Vector3.Angle(this.eyeOriginalTransform.forward, direction);
					if (d < closestDistance && angle < 70f)
					{
						closestPlayer = p;
						closestDistance = d;
					}
				}

				if (closestPlayer != null)
				{
					Transform target = closestPlayer.gameplayCamera.transform;

					float distance = Vector3.Distance(this.eyeTransform.position, target.position);
					if (distance < 7f)
					{
						Vector3 directionToTarget = target.position - this.eyeTransform.position;
						Quaternion desiredRotation = Quaternion.LookRotation(directionToTarget, this.eyeTransform.up);
						Quaternion limitedDesiredRotation = Quaternion.RotateTowards(this.eyeOriginalTransform.rotation, desiredRotation, 40f);

						float angle = Quaternion.Angle(desiredRotation, this.eyeOriginalTransform.rotation);

						if (angle < 90f)
						{
							this.eyeTransform.rotation = Quaternion.RotateTowards(this.eyeTransform.rotation, limitedDesiredRotation, 100f * Time.deltaTime); // make eye look towards target
							return;
						}
					}
				}
				this.eyeTransform.rotation = Quaternion.RotateTowards(this.eyeTransform.rotation, this.eyeOriginalTransform.rotation, 20f * Time.deltaTime); // make eye look forward
			}
		}

		public override void Update()
		{
			base.Update();

			if (this.isEnemyDead)
			{
				this.currentBehaviourStateIndex = 7;
			}
			else if(this.currentBehaviourStateIndex == 0 || this.currentBehaviourStateIndex == 1 || this.currentBehaviourStateIndex == 5)
			{
				CalculateAnimationDirection();
			}

			if (this.currentBehaviourStateIndex == 0) // Neutral State
			{
				PutCreatureOnGround();

				if (!this.IsOwner) { return; } // if we're the owner

				this.stateTimer += Time.deltaTime;
				if (this.stateTimer < 0.15f) { return; } // every 0.15 seconds we
				this.stateTimer = 0f;

				PlayerControllerB p = base.CheckLineOfSightForClosestPlayer(70f, sightRange, 3);
				if (p == null) { return; }
				if(Vector3.Distance(base.eye.position, p.gameplayCamera.transform.position) > sightRange) { return; } 

				PlayCreatureSFXServerRpc(0);
				BeginChasingPlayerServerRpc((int)p.playerClientId);
				SwitchBehaviourState(1); // switch to Chasing State
			}
			else if (this.currentBehaviourStateIndex == 1) // Chasing State
			{
				PutCreatureOnGround();

				if (!this.IsOwner) { return; } // if we're the owner

				if (this.stateTimer2 < 0.5f)
				{
					this.stateTimer2 += Time.deltaTime; // don't jump until at least half a second has passed
					return;
				}

				if (base.targetPlayer != null)
				{
					if (Vector3.Distance(base.targetPlayer.playerGlobalHead.transform.position, this.eye.position) < 8f) // if the peeper is close enough
					{
						Vector3 directionToTarget = base.targetPlayer.playerGlobalHead.transform.position - this.eye.position;
						directionToTarget.y = 0f;
						Vector3 forwardDirection = this.eye.forward;
						forwardDirection.y = 0f;

						if (Vector3.Angle(forwardDirection, directionToTarget) < 15f) // If the peeper is facing you directly
						{
							PlayCreatureSFXServerRpc(3);
							SwitchBehaviourState(3); // switch to Jumping State
							JumpAtPlayerServerRpc(this.targetPlayer.playerClientId);
							return;
						}
					}
				}

				this.stateTimer += Time.deltaTime;
				if (this.stateTimer < 0.15f) { return; } // every 0.15 seconds we
				this.stateTimer = 0f;

				PlayerControllerB p = base.CheckLineOfSightForClosestPlayer(70f, sightRange, 3); // check if a player is still in sight

				if (p == null || (Vector3.Distance(base.eye.position, p.gameplayCamera.transform.position) > sightRange) )
				{
					stateCounter++;
					if (stateCounter < 8) { return; }
					stateCounter = 0;

					SwitchBehaviourState(0); // switch to Neutral State
				}
				else if (p != GameNetworkManager.Instance.localPlayerController) // if the closest player is someone else
				{
					BeginChasingPlayerServerRpc((int)p.playerClientId);
				}
			}
			else if (this.currentBehaviourStateIndex == 2) // Spawning State
			{
				PutCreatureOnGround();

				if (!this.IsOwner) { return; } // if we're the owner

				this.stateTimer += Time.deltaTime;
				if (this.stateTimer > 5.5f)
				{
					stateTimer = 0;
					if (base.targetPlayer != null)
					{
						SwitchBehaviourState(1); // switch to Chase State
					}
					else
					{
						SwitchBehaviourState(0); // switch to Neutral State
					}
				}

				this.stateTimer2 += Time.deltaTime;
				if (this.stateTimer2 < 0.5f) { return; } // every 0.5 seconds we
				this.stateTimer2 = 0f;

				PlayerControllerB p = base.CheckLineOfSightForClosestPlayer(70f, sightRange, 3);

				if (p == null) { return; }
				if (Vector3.Distance(base.eye.position, p.gameplayCamera.transform.position) > sightRange) { return; }

				BeginChasingPlayerServerRpc((int)p.playerClientId);
			}
			else if (this.currentBehaviourStateIndex == 3) // Jumping State
			{
				if (!this.IsOwner)
				{
					this.transform.position = base.serverPosition;
					return;
				}
				base.SyncPositionToClients(); // we need to sync position when it's a phsyics object, won't happen automatically because inSpecialAnimation is true

				if (stateCounter == 0)
				{
					this.stateTimer += Time.deltaTime;
					if (this.stateTimer > 2.5f)
					{
						this.hitboxCollider.radius = 0.5f; // after 2.5 seconds shrink the collider back down, physics collider can interact with other enemies again
						this.physicsCollider.includeLayers |= 1 << 19; // add Enemies layer to inclusions
						this.physicsCollider.excludeLayers &= ~(1 << 19); // remove Enemies layer from exclusions
						stateCounter = 1;
					}
				}

				this.stateTimer2 += Time.deltaTime;
				if (this.stateTimer2 < 5f) { return; } // after 5 seconds we
				this.stateTimer2 = 0f;

				PlayCreatureSFXServerRpc(0, 0.5f);
				SwitchBehaviourState(5); // switch to Recovery State
			}
			else if (this.currentBehaviourStateIndex == 4) // Attached State
			{
				base.SyncPositionToClients();
			}
			else if (this.currentBehaviourStateIndex == 5) // Recovery State
			{
				PutCreatureOnGround();

				if (!this.IsOwner) { return; } // if we're the owner

				this.stateTimer += Time.deltaTime;
				if (this.stateTimer < 1.2f) { return; }
				this.stateTimer = 0f;

				SwitchBehaviourState(0); // switch to Neutral State
			}
			else if (this.currentBehaviourStateIndex == 6) // Fall Off State
			{
				if (!this.IsOwner)
				{
					this.transform.position = base.serverPosition;
					return;
				}
				base.SyncPositionToClients(); // we need to sync position when it's a phsyics object, won't happen automatically because inSpecialAnimation is true

				this.stateTimer += Time.deltaTime;
				if (this.stateTimer < 5f) { return; } // after 5 seconds we
				this.stateTimer = 4.5f;

				if (this.physicsRigidbody.velocity.sqrMagnitude > 10f) { return; } // don't recover if the ball is still moving

				PlayCreatureSFXServerRpc(0, 0.5f);
				SwitchBehaviourState(5); // switch to Recovery State
			}
			else if (this.currentBehaviourStateIndex == 7) // Dead State
			{
				if (ragdollFrozen) { return; }
				this.stateTimer += Time.deltaTime;
				if (this.stateTimer < 15f) { return; } // after 10 seconds we freeze the rigidbodies for performance
				this.stateTimer = 0f;

				foreach (Rigidbody rb in this.ragdollRigidbodies)
				{
					rb.isKinematic = true;
					ragdollFrozen = true;
				}
			}
		}

		[ServerRpc(RequireOwnership = false)]
		public void JumpAtPlayerServerRpc(ulong playerObjectId)
		{
			this.JumpAtPlayerClientRpc(playerObjectId);
		}

		[ClientRpc]
		public void JumpAtPlayerClientRpc(ulong playerObjectId)
		{
			this.JumpAtPlayer(StartOfRound.Instance.allPlayerScripts[(int)(checked((IntPtr)playerObjectId))]);
		}

		private void JumpAtPlayer(PlayerControllerB playerScript)
		{
			if (base.isEnemyDead) { return; }
			if (this.isAttached) { return; }

			Debug.Log("Peeper JumpAtPlayer 1");

			base.agent.speed = 0f;
			
			this.creatureModel.transform.localPosition = Vector3.zero;
			
			base.creatureAnimator.SetBool("Jump", true);
			base.creatureAnimator.SetBool("Attached", false);
			base.creatureAnimator.SetBool("Dead", false);

			this.isAttached = false;
			this.inSpecialAnimation = true;

			this.agent.enabled = false;
			this.enemyType.canBeStunned = true;

			this.attachCollider.enabled = true;
			this.physicsCollider.enabled = true;
			this.physicsRigidbody.isKinematic = false;
			this.hitboxCollider.enabled = true;

			this.hitboxCollider.radius = 2f; // They're too fast to hit normally, make their hitboxes larger when they're jumping so you can perfect counter them

			Debug.Log("Peeper JumpAtPlayer 2");

			Vector3 targetPosition = playerScript.playerGlobalHead.transform.position;

			// Predict where the player is moving when jumping at them
            if (this.targetPlayer == GameNetworkManager.Instance.localPlayerController)
            {
				targetPosition += this.targetPlayer.thisController.velocity / 5f;
			}
            else if (this.targetPlayer.timeSincePlayerMoving < 0.25f)
            {
                targetPosition += Vector3.Normalize((this.targetPlayer.serverPlayerPosition - this.targetPlayer.oldPlayerPosition) * 100f);
            }

			Debug.Log("Peeper JumpAtPlayer 3");

			this.physicsRigidbody.velocity = 33f * (targetPosition - this.transform.position + Vector3.up).normalized;

			creatureAnimator.SetBool("Grounded", false);
		}

		[ServerRpc(RequireOwnership = false)]
		public void BeginChasingPlayerServerRpc(int playerObjectId)
		{
			this.BeginChasingPlayerClientRpc(playerObjectId);
		}

		[ClientRpc]
		public void BeginChasingPlayerClientRpc(int playerObjectId)
		{
			this.BeginChasingPlayer(playerObjectId);
		}

		public void BeginChasingPlayer(int playerObjectId)
		{
			PlayerControllerB playerScript = StartOfRound.Instance.allPlayerScripts[playerObjectId];
			base.targetPlayer = playerScript;
			base.ChangeOwnershipOfEnemy((ulong)playerObjectId);
			base.SetMovingTowardsTargetPlayer(playerScript);
		}

		[ServerRpc(RequireOwnership = false)]
		public void PlayCreatureSFXServerRpc(int index, float volume = 1f)
		{
			this.PlayCreatureSFXClientRpc(index, volume);
		}

		[ClientRpc]
		public void PlayCreatureSFXClientRpc(int index, float volume = 1f)
		{
			this.PlayCreatureSFX(index, volume);
		}

		private void PlayCreatureSFX(int index, float volume = 1f)
		{
			this.creatureVoice.pitch = UnityEngine.Random.Range(0.8f, 1.1f);
			this.AttachSFXSource.pitch = UnityEngine.Random.Range(0.9f, 1.05f);
			AudioClip clip;
			switch (index)
			{
				case 0: // spotSFX
					clip = this.spotSFX[UnityEngine.Random.Range(0, this.spotSFX.Length)];
					this.creatureVoice.PlayOneShot(clip, volume);
					WalkieTalkie.TransmitOneShotAudio(this.creatureVoice, clip, volume);
					break;
				case 1: // walkSFX
					this.creatureSFX.PlayOneShot(this.walkSFX, volume * 0.22f);
                    WalkieTalkie.TransmitOneShotAudio(base.creatureSFX, this.walkSFX, volume * 0.22f);
                    break;
				case 2: // runSFX
					this.creatureSFX.PlayOneShot(this.runSFX, volume * 0.25f);
                    WalkieTalkie.TransmitOneShotAudio(base.creatureSFX, this.runSFX, volume * 0.25f);
                    break;
				case 3: // jumpSFX
					clip = this.jumpSFX[UnityEngine.Random.Range(0, this.jumpSFX.Length)];
					this.creatureVoice.PlayOneShot(clip, volume);
					WalkieTalkie.TransmitOneShotAudio(this.creatureVoice, clip, volume);
					break;
				case 4: // attachSFX
					clip = this.attachSFX[0];
					this.AttachSFXSource.PlayOneShot(clip, volume);
					WalkieTalkie.TransmitOneShotAudio(this.AttachSFXSource, clip, volume);
					break;
				case 5: // deathSFX
					clip = this.deathSFX[UnityEngine.Random.Range(0, this.deathSFX.Length)];
					this.creatureVoice.PlayOneShot(clip, volume);
					WalkieTalkie.TransmitOneShotAudio(this.creatureVoice, clip, volume);
					break;
				case 6: // ejectSFX
					clip = this.ejectSFX[UnityEngine.Random.Range(0, this.ejectSFX.Length)];
					this.creatureVoice.PlayOneShot(clip, volume);
					WalkieTalkie.TransmitOneShotAudio(this.creatureVoice, clip, volume);
					break;
				default:
					break;
			}
		}

		private float peeperWeight = 0.1f;
		private bool isWeightedInternal = false;
		public bool IsWeighted
		{
			get
			{
				return this.isWeightedInternal;
			}
			set
			{
				if (value == true && this.isWeightedInternal == false)
				{
					this.attachedPlayer.carryWeight += peeperWeight;
				}
				else if (value == false && this.isWeightedInternal == true)
				{
					this.attachedPlayer.carryWeight += -peeperWeight;
				}
				this.isWeightedInternal = value;
			}
		}

		[ServerRpc(RequireOwnership = false)]
        public void AttachToPlayerServerRpc(ulong playerObjectId)
        {
            this.AttachToPlayerClientRpc(playerObjectId);
        }

        [ClientRpc]
        public void AttachToPlayerClientRpc(ulong playerObjectId)
        {
            this.AttachToPlayerLocally(StartOfRound.Instance.allPlayerScripts[(int)(checked((IntPtr)playerObjectId))]);
        }

        private void AttachToPlayerLocally(PlayerControllerB playerScript)
		{
			if (base.isEnemyDead) { return; }
			if (this.isAttached) { return; }
			if (playerScript.isPlayerDead) { return; }

			int attachedCount = 0;
			foreach (PeeperAI peeper in Peeper.PeeperList)
			{
				if (peeper.attachedPlayer == playerScript)
				{
					attachedCount++;
				}
			}
			if (attachedCount >= 10) { return; } // only attach a max of 10 peepers

			this.isAttached = true;
			this.targetPlayer = playerScript;

			this.attachedPlayer = playerScript;
			if (!playerScript.isPlayerDead)
			{
				IsWeighted = true;
			}

			if (playerScript == GameNetworkManager.Instance.localPlayerController)
			{
				HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
			}

			base.ChangeOwnershipOfEnemy(playerScript.actualClientId);

			SwitchBehaviourStateLocally(4);

			PlayCreatureSFX(4);

			float randomXRotation = 0f, randomYRotation = 0f, randomZRotation = 0f;
			Transform target = null;

			// Choose a random part of the body to attach to
			UnityEngine.Random.InitState(base.thisEnemyIndex + StartOfRound.Instance.randomMapSeed);
			int targetIndex = UnityEngine.Random.Range(0, 11);
			for (int i = 0; i < 10; i++)
			{
				if (targetIndex == 6) { targetIndex++; } // skip 6
				if (!UsedAttachTargets.Contains(this.attachedPlayer.bodyParts[targetIndex]))
				{
					if (targetIndex == 0 && this.attachedPlayer == GameNetworkManager.Instance.localPlayerController)
					{
						target = this.attachedPlayer.gameplayCamera.transform; // don't let the peeper block your camera locally
					}
					else
					{
						target = this.attachedPlayer.bodyParts[targetIndex];
					}
					UsedAttachTargets.Add(target);
					this.attachTargetTransform = target;
					break;
				}
				else
				{
					if (targetIndex == 10)
					{
						targetIndex = 0;
					}
					else
					{
						targetIndex++;
					}
				}
			}
			if (target == null) { return; }

			switch (targetIndex)
			{
				case 0:
					randomXRotation = UnityEngine.Random.Range(-30f, -90f);
					randomYRotation = UnityEngine.Random.Range(80f, 280f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0.35f, 0.2f);
					break;

				case 1:
					randomYRotation = UnityEngine.Random.Range(60f, 160f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.075f);
					break;

				case 2:
					randomYRotation = UnityEngine.Random.Range(-60f, -160f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.075f);
					break;

				case 3:
					randomYRotation = UnityEngine.Random.Range(0f, 160f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.2f);
					break;

				case 4:
					randomYRotation = UnityEngine.Random.Range(0f, -160f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.2f);
					break;

				case 5:
					randomYRotation = UnityEngine.Random.Range(-30f, 30f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.2f);
					break;

				case 7:
					randomYRotation = UnityEngine.Random.Range(0f, 160f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.2f);
					break;

				case 8:
					randomYRotation = UnityEngine.Random.Range(0f, -160f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.2f);
					break;

				case 9:
					randomYRotation = UnityEngine.Random.Range(160f, 280f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.15f);
					break;

				case 10:
					randomYRotation = UnityEngine.Random.Range(-20f, 100f);
					randomZRotation = UnityEngine.Random.Range(0f, 360f);
					this.attachTargetTranslationOffset = new Vector3(0f, 0f, 0.15f);
					break;

				default:
					Debug.Log("Peepers have encountered an error when attaching! Report it to the developer! Target Index: " + targetIndex);
					break;
			}
			this.attachTargetRotationOffset = new Vector3(randomXRotation, randomYRotation, randomZRotation);
			this.creatureModel.transform.localPosition = Vector3.zero;
		}

		[ServerRpc(RequireOwnership = false)]
		public void EjectFromPlayerServerRpc(ulong playerObjectId)
		{
			this.EjectFromPlayerClientRpc(playerObjectId);
		}

		[ClientRpc]
		public void EjectFromPlayerClientRpc(ulong playerObjectId)
		{
			this.EjectFromPlayer(StartOfRound.Instance.allPlayerScripts[(int)(checked((IntPtr)playerObjectId))]);
		}

		public void EjectFromPlayer(PlayerControllerB playerScript)
		{
			if (!this.isAttached) { return; }
			this.isAttached = false;

			if (!playerScript.isPlayerDead)
			{
				IsWeighted = false;
			}
			this.attachedPlayer = null;

			UsedAttachTargets.Remove(this.attachTargetTransform);
			this.attachTargetTransform = null;

			if (!base.isEnemyDead)
			{
				SwitchBehaviourStateLocally(6); // switch to Fall Off State
			}

            Vector3 direction = playerScript.transform.position - this.transform.position;
            direction.y = 0f;
            direction = direction.normalized;
            direction.y = -1f;
            this.physicsRigidbody.AddForce(-10f * direction, ForceMode.Impulse);

			PlayCreatureSFX(6);
        }
	}

	public class PeeperAttachHitbox : MonoBehaviour
	{
		public PeeperAI mainScript;
		private void OnTriggerEnter(Collider other)
		{
			if (this.mainScript.isEnemyDead) { return; }
			if (this.mainScript.isAttached) { return; }

			if (other.CompareTag("Player"))
			{
				PlayerControllerB playerControllerB = other.gameObject.GetComponent<PlayerControllerB>();
				if (playerControllerB != null && playerControllerB.IsOwner)
				{
					this.mainScript.AttachToPlayerServerRpc(playerControllerB.playerClientId);
				}
			}
		}
	}
}