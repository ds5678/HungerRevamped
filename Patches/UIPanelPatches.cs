﻿using System;
using Harmony;
using UnityEngine;

namespace HungerRevamped {

	/*
	 * Lots of very similar code here because Hinterland creates lots of almost identical GUIs - but all
	 * of them are just dissimilar enough that code can't be reused across different GUI classes...
	 */

	internal class UIPanelPatches {

		// Panel_FirstAid

		[HarmonyPatch(typeof(Panel_FirstAid), "Start")]
		private static class AddStoredCaloriesLabel {
			private static void Prefix(Panel_FirstAid __instance) {
				Transform airTempRow = __instance.m_AirTempLabel.transform.parent;
				Transform windChillRow = __instance.m_WindchillLabel.transform.parent;
				Transform feelsLikeRow = __instance.m_FeelsLikeLabel.transform.parent;

				GameObject storedCaloriesRow = NGUITools.AddChild(windChillRow.parent.gameObject, windChillRow.gameObject);
				storedCaloriesRow.name = "StoredCaloriesWarmthLabel";
				UISprite icon = storedCaloriesRow.transform.Find("Stat Icon").GetComponent<UISprite>();
				icon.spriteName = "ico_HUD_hunger"; //"ico_Radial_food2";
				UILocalize nameLabel = storedCaloriesRow.transform.Find("Stat Label").GetComponent<UILocalize>();
				nameLabel.key = "GAMEPLAY_CalorieStore";

				const float basePos = 24;
				airTempRow.transform.localPosition = new Vector3(0, basePos, 0);
				windChillRow.transform.localPosition = new Vector3(0, basePos - 26, 0);
				storedCaloriesRow.transform.localPosition = new Vector3(0, basePos - 52, 0);
				feelsLikeRow.transform.localPosition -= new Vector3(0, 5, 0);
			}
		}

		[HarmonyPatch(typeof(Panel_FirstAid), "RefreshStatusLabels")]
		private static class UpdateCalorieStoreDisplay {
			private static void Postfix(Panel_FirstAid __instance) {
				// Replace hunger bar calories with stored calories
				double storedCalories = HungerRevamped.Instance.storedCalories;
				__instance.m_CalorieStoreLabel.text = Convert.ToInt64(storedCalories).ToString();
				__instance.m_CalorieStoreLabel.color = ((storedCalories > 250.0) ? Color.white : __instance.m_PoorHealthStatusColor);

				// Adjust stored calories freezing bonus
				Transform container = __instance.m_WindchillLabel.gameObject.transform.parent.parent;
				UILabel label = container.Find("StoredCaloriesWarmthLabel/Stat Value").GetComponent<UILabel>();
				float warmthBonus = HungerRevamped.Instance.GetStoredCaloriesWarmthBonus();
				label.text = Utils.GetTemperatureString(warmthBonus, true, true, false);
				label.color = (warmthBonus >= 0) ? Color.white : __instance.m_PoorHealthStatusColor;
			}
		}

		// Panel_Rest

		[HarmonyPatch(typeof(Panel_Rest), "UpdateVisuals")]
		private static class PredictHungerAfterRest {
			private static HungerTuple lastSimulation;

			private static void Postfix(Panel_Rest __instance) {
				PlayerManager playerManager = GameManager.GetPlayerManagerComponent();
				Hunger hunger = GameManager.GetHungerComponent();

				float calorieBurnRate;
				if ((bool) AccessTools.Field(typeof(Panel_Rest), "m_ShowPassTime").GetValue(__instance)) {
					calorieBurnRate = playerManager.CalculateModifiedCalorieBurnRate(hunger.m_CalorieBurnPerHourStanding);
				} else {
					calorieBurnRate = playerManager.CalculateModifiedCalorieBurnRate(hunger.m_CalorieBurnPerHourSleeping);
				}

				HungerTuple simulation = lastSimulation;
				if (!GameManager.GetPassTime().IsPassingTime()) {
					int sleepHours = (int) AccessTools.Field(typeof(Panel_Rest), "m_SleepHours").GetValue(__instance);
					simulation = HungerRevamped.Instance.SimulateHungerBar(calorieBurnRate, sleepHours);
					lastSimulation = simulation;
				}

				UILabel storedCaloriesLabel = __instance.m_CalorieReservesLabel;
				int storedCalories = Mathf.RoundToInt(simulation.storedCalories);
				storedCaloriesLabel.text = storedCalories.ToString();

				UILabel hungerPercentLabel = __instance.m_EstimatedCaloriesBurnedLabel.GetComponent<UILabel>();
				int hungerPercent = Mathf.FloorToInt(simulation.hungerRatio * 100f);
				hungerPercentLabel.text = hungerPercent.ToString() + "%";
				hungerPercentLabel.color = (simulation.hungerRatio >= Tuning.hungerLevelStarving) ? Color.white : __instance.m_NegativeTemperatureColor;
			}
		}

		[HarmonyPatch(typeof(Panel_Rest), "Start", new Type[0])]
		private static class ChangeRestHungerLabel {
			private static void Postfix(Panel_Rest __instance) {
				Transform parentTransform = __instance.m_EstimatedCaloriesBurnedLabel.transform.parent;
				GameObject gameObject = parentTransform.Find("Label_CaloriesBurned").gameObject;
				UILocalize localize = gameObject.GetComponent<UILocalize>();
				localize.key = "GAMEPLAY_Hunger";
			}
		}

		// Panel_BodyHarvest

		[HarmonyPatch(typeof(Panel_BodyHarvest), "RefreshCalorieLabels")]
		private static class PredictHungerAfterBodyHarvest {
			private static bool Prefix(Panel_BodyHarvest __instance) {
				float origBurnRate = GameManager.GetHungerComponent().m_CalorieBurnPerHourHarvestingCarcass;
				float calorieBurnRate = GameManager.GetPlayerManagerComponent().CalculateModifiedCalorieBurnRate(origBurnRate);
				bool harvest = (bool) InvokeMethod(__instance, "IsTabHarvestSelected");
				bool quarter = (bool) InvokeMethod(__instance, "IsTabQuarterSelected");

				UILabel targetLabel = null;
				float hours = 0;

				if (harvest) {
					targetLabel = __instance.m_Label_HarvestEstimatedCalories;
					hours = (float) InvokeMethod(__instance, "GetHarvestDurationMinutes") / 60f;
				} else if (quarter) {
					targetLabel = __instance.m_Label_QuarterEstimatedCalories;
					hours = (float) InvokeMethod(__instance, "GetQuarterDurationMinutes") / 60f;
				}

				HungerTuple simulation = HungerRevamped.Instance.SimulateHungerBar(calorieBurnRate, hours);
				int hungerPercent = Mathf.RoundToInt(simulation.hungerRatio * 100f);
				if (targetLabel != null)
					targetLabel.text = hungerPercent + "% " + Localization.Get("GAMEPLAY_Hunger");

				__instance.m_Label_CurrentCalories.text = Localization.Get("GAMEPLAY_CalorieStore");
				int storedCalories = Mathf.RoundToInt(simulation.storedCalories);
				__instance.m_Label_CurrentCaloriesAmount.text = storedCalories.ToString();

				return false;
			}

			private static object InvokeMethod(Panel_BodyHarvest panel, string methodName) {
				return AccessTools.Method(typeof(Panel_BodyHarvest), methodName).Invoke(panel, new object[0]);
			}
		}

		// Panel_BreakDown

		private static void UpdateBreakDownPanel(Panel_BreakDown panel) {
			float hours = (float) AccessTools.Field(typeof(Panel_BreakDown), "m_DurationHours").GetValue(panel);
			float origBurnRate = GameManager.GetHungerComponent().m_CalorieBurnPerHourBreakingDown;
			float calorieBurnRate = GameManager.GetPlayerManagerComponent().CalculateModifiedCalorieBurnRate(origBurnRate);
			HungerTuple simulation = HungerRevamped.Instance.SimulateHungerBar(calorieBurnRate, hours);

			panel.m_CurrentCaloriesLabel.text = Localization.Get("GAMEPLAY_CalorieStore");
			UILabel calorieStoreLabel = panel.m_CurrentCaloriesValLabel;
			int storedCalories = Mathf.RoundToInt(simulation.storedCalories);
			calorieStoreLabel.text = storedCalories.ToString();
			calorieStoreLabel.UpdateAnchors();

			UILabel hungerLabel = panel.m_EstimatedCaloriesBurnedLabel;
			int hungerPercent = Mathf.RoundToInt(simulation.hungerRatio * 100f);
			hungerLabel.text = hungerPercent.ToString() + "% " + Localization.Get("GAMEPLAY_Hunger");
		}

		[HarmonyPatch(typeof(Panel_BreakDown), "UpdateCurrentCaloriesLabel")]
		private static class DisableBreakDownUpdateCurrentCaloriesLabel {
			private static bool Prefix(Panel_BreakDown __instance) {
				UpdateBreakDownPanel(__instance);
				return false;
			}
		}

		[HarmonyPatch(typeof(Panel_IceFishing), "UpdateEstimatedCaloriesBurnedLabel")]
		private static class DisableBreakDownUpdateEstimatedCaloriesBurnedLabel {
			private static bool Prefix() {
				return false;
			}
		}

		// Panel_IceFishing

		private static void UpdateIceFishingPanel(Panel_IceFishing panel) {
			int hours = (int) AccessTools.Field(typeof(Panel_IceFishing), "m_HoursToFish").GetValue(panel);
			float origBurnRate = GameManager.GetHungerComponent().m_CalorieBurnPerHourStanding;
			float calorieBurnRate = GameManager.GetPlayerManagerComponent().CalculateModifiedCalorieBurnRate(origBurnRate);
			HungerTuple simulation = HungerRevamped.Instance.SimulateHungerBar(calorieBurnRate, hours);

			UILabel calorieStoreLabel = panel.m_CurrentCaloriesLabel.GetComponent<UILabel>();
			int storedCalories = Mathf.RoundToInt(simulation.storedCalories);
			calorieStoreLabel.text = Localization.Get("GAMEPLAY_CalorieStore") + ": " + storedCalories.ToString();

			UILabel hungerLabel = panel.m_EstimatedCaloriesBurnedLabel.GetComponent<UILabel>();
			int hungerPercent = Mathf.RoundToInt(simulation.hungerRatio * 100f);
			hungerLabel.text = Localization.Get("GAMEPLAY_Hunger") + ": " + hungerPercent.ToString() + "%";
		}

		[HarmonyPatch(typeof(Panel_IceFishing), "Enable")]
		private static class PredictHungerAfterIceFishingEnable {
			private static void Postfix(Panel_IceFishing __instance, bool enable) {
				if (enable) {
					UpdateIceFishingPanel(__instance);
				}
			}
		}

		[HarmonyPatch(typeof(Panel_IceFishing), "UpdateCurrentCaloriesLabel")]
		private static class DisableIceFishingUpdateCurrentCaloriesLabel {
			private static bool Prefix(Panel_IceFishing __instance) {
				UpdateIceFishingPanel(__instance);
				return false;
			}
		}

		[HarmonyPatch(typeof(Panel_IceFishing), "UpdateEstimatedCaloriesBurnedLabel")]
		private static class DisableIceFishingUpdateEstimatedCaloriesBurnedLabel {
			private static bool Prefix() {
				return false;
			}
		}

		// Panel_SnowShelterBuild

		[HarmonyPatch(typeof(Panel_SnowShelterBuild), "Start")]
		private static class SetSnowShelterBuildLabels {
			private static void Postfix(Panel_SnowShelterBuild __instance) {
				UILocalize calorieStoreHeader = __instance.m_CurrentCaloriesLabel.GetComponent<UILocalize>();
				calorieStoreHeader.key = "GAMEPLAY_CalorieStore";

				Transform estimatedCaloriesHeader = __instance.m_EstimatedCaloriesBurnedLabel.transform.parent.Find("Header");
				UILocalize localize = estimatedCaloriesHeader.GetComponent<UILocalize>();
				localize.key = "GAMEPLAY_Hunger";
			}
		}

		private static void UpdateSnowShelterBuildPanel(Panel_SnowShelterBuild panel) {
			float hours = (float) AccessTools.Field(typeof(Panel_SnowShelterBuild), "m_DurationHours").GetValue(panel);
			float origBurnRate = GameManager.GetHungerComponent().m_CalorieBurnPerHourBuildingSnowShelter;
			float calorieBurnRate = GameManager.GetPlayerManagerComponent().CalculateModifiedCalorieBurnRate(origBurnRate);
			HungerTuple simulation = HungerRevamped.Instance.SimulateHungerBar(calorieBurnRate, hours);

			UILabel calorieStoreLabel = panel.m_CurrentCaloriesValLabel.GetComponent<UILabel>();
			int storedCalories = Mathf.RoundToInt(simulation.storedCalories);
			calorieStoreLabel.text = storedCalories.ToString();

			UILabel hungerLabel = panel.m_EstimatedCaloriesBurnedLabel.GetComponent<UILabel>();
			int hungerPercent = Mathf.RoundToInt(simulation.hungerRatio * 100f);
			hungerLabel.text = hungerPercent.ToString() + "%";
		}

		[HarmonyPatch(typeof(Panel_SnowShelterBuild), "UpdateCurrentCaloriesLabel")]
		private static class DisableSnowShelterBuildUpdateCurrentCaloriesLabel {
			private static bool Prefix(Panel_SnowShelterBuild __instance) {
				UpdateSnowShelterBuildPanel(__instance);
				return false;
			}
		}

		[HarmonyPatch(typeof(Panel_SnowShelterBuild), "UpdateEstimatedCaloriesBurnedLabel")]
		private static class DisableSnowShelterBuildUpdateEstimatedCaloriesBurnedLabel {
			private static bool Prefix() {
				return false;
			}
		}

		// Panel_SnowShelterInteract

		[HarmonyPatch(typeof(Panel_SnowShelterInteract), "Start")]
		private static class SetSnowShelterInteractLabels {
			private static void Postfix(Panel_SnowShelterInteract __instance) {
				UILocalize calorieStoreHeader = __instance.m_CurrentCaloriesLabel.GetComponent<UILocalize>();
				calorieStoreHeader.key = "GAMEPLAY_CalorieStore";

				Transform estimatedCaloriesHeader = __instance.m_EstimatedCaloriesBurnedLabel.transform.parent.Find("Header");
				UILocalize localize = estimatedCaloriesHeader.GetComponent<UILocalize>();
				localize.key = "GAMEPLAY_Hunger";
			}
		}

		private static void UpdateSnowShelterInteractPanel(Panel_SnowShelterInteract panel) {
			float hours = (float) AccessTools.Method(typeof(Panel_SnowShelterInteract), "GetTaskDurationInHours").Invoke(panel, new object[0]);
			float origBurnRate = (float) AccessTools.Method(typeof(Panel_SnowShelterInteract), "GetCalorieBurnRate").Invoke(panel, new object[0]);
			float calorieBurnRate = GameManager.GetPlayerManagerComponent().CalculateModifiedCalorieBurnRate(origBurnRate);
			HungerTuple simulation = HungerRevamped.Instance.SimulateHungerBar(calorieBurnRate, hours);

			UILabel calorieStoreLabel = panel.m_CurrentCaloriesValLabel.GetComponent<UILabel>();
			int storedCalories = Mathf.RoundToInt(simulation.storedCalories);
			calorieStoreLabel.text = storedCalories.ToString();

			UILabel hungerLabel = panel.m_EstimatedCaloriesBurnedLabel.GetComponent<UILabel>();
			int hungerPercent = Mathf.RoundToInt(simulation.hungerRatio * 100f);
			hungerLabel.text = hungerPercent.ToString() + "%";
		}

		[HarmonyPatch(typeof(Panel_SnowShelterInteract), "UpdateCurrentCaloriesLabel")]
		private static class DisableSnowShelterInteractUpdateCurrentCaloriesLabel {
			private static bool Prefix(Panel_SnowShelterInteract __instance) {
				UpdateSnowShelterInteractPanel(__instance);
				return false;
			}
		}

		[HarmonyPatch(typeof(Panel_SnowShelterInteract), "UpdateEstimatedCaloriesBurnedLabel")]
		private static class DisableSnowShelterInteractUpdateEstimatedCaloriesBurnedLabel {
			private static bool Prefix() {
				return false;
			}
		}
	}
}
