using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using BepInEx.Configuration;

namespace BetterCraneNS;

[BepInPlugin("BetterCrane", "BetterCrane", "0.1.0")]
public class BetterCranePlugin : BaseUnityPlugin
{
	public static ManualLogSource L;

	public static Harmony HarmonyInstance;
	enum WhatCardsCranable
	{
		SameAsBaseGame,
		DraggableButBuildings,
		Draggable,
		All
	}
	private static ConfigEntry<bool> canMoveOccupiedCards;
	private static ConfigEntry<bool> canTargetSellBuy;
	private static ConfigEntry<bool> canUnloadBoosters;
	private static ConfigEntry<WhatCardsCranable> canMoveWhat;
	private void Awake()
	{
		L = ((BetterCranePlugin)this).Logger;
		
		try
		{
			HarmonyInstance = new Harmony("BetterCranePlugin");
			HarmonyInstance.PatchAll(typeof(BetterCranePlugin));
		}
		catch (Exception ex3)
		{
			Log("Patching failed: " + ex3.Message);
		}
		string description = "";
		description = $"Anable cranes to move cards that are in a process of doing something.";
		canMoveOccupiedCards = Config.Bind("Allow cranes to", "Move occupied cards", false, description);
		//canMoveOccupiedCards.SettingChanged += (_1, _2) => UpdateSidebarHeights();

		description = $"Anable cranes to target sell and buy boxes.";
		canTargetSellBuy = Config.Bind("Allow cranes to", "Target sell and buy boxes", true, description);
		//canTargetSellBuy.SettingChanged += (_1, _2) => UpdateSidebarHeights();

		description = $"Anable cranes pull cards out of booster packs.";
		canUnloadBoosters = Config.Bind("Allow cranes to", "Unload booster packs", true, description);
		//canUnloadBoosters.SettingChanged += (_1, _2) => UpdateSidebarHeights();

		description = $"What card types are cranable";
		canMoveWhat = Config.Bind("Allow cranes to", "move which cards", WhatCardsCranable.DraggableButBuildings, description);
		//canMoveWhat.SettingChanged += (_1, _2) => UpdateSidebarHeights();

		CreateConfigWatcher();

	}
	private void CreateConfigWatcher() {
		FileSystemWatcher watcher = new FileSystemWatcher(Paths.ConfigPath, Path.GetFileName(Config.ConfigFilePath));
		watcher.Changed += (_1, _2) => Config.Reload();
		watcher.Created += (_1, _2) => Config.Reload();
		watcher.Renamed += (_1, _2) => Config.Reload();
		watcher.EnableRaisingEvents = true;
		watcher.IncludeSubdirectories = true;
		watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
	}
	public static void Log(string s)
	{
		L.LogInfo((object)(DateTime.Now.ToString("HH:MM:ss") + ": " + s));
	}
	public static Vector3 getDirectionVector(Conveyor conveyor)
	{
		if (conveyor.Direction == 0)
		{
			return Vector3.back;
		}
		if (conveyor.Direction == 1)
		{
			return Vector3.left;
		}
		if (conveyor.Direction == 2)
		{
			return Vector3.forward;
		}
		if (conveyor.Direction == 3)
		{
			return Vector3.right;
		}
		return Vector3.back;
	}
	[HarmonyPatch(typeof(Conveyor), "UpdateCard")]
	[HarmonyPrefix]
	private static bool Conveyor_UpdateCard_Prefix(ref Conveyor __instance)
	{
		if (__instance.MyGameCard.IsDemoCard)
		{
			return false;
		}
		bool flag = true;
		if (__instance.MyGameCard.Velocity.HasValue)
		{
			flag = false;
		}
		Draggable drag = null;
		if (flag)
		{
			drag = GetInputDraggable(__instance, getDirectionVector(__instance), allowDraggingCards: true);
		}
		Draggable output = null;
		if (drag is GameCard gc)
		{
			CardData cardData = gc.GetLeafCard().CardData;
			if (cardData != null && InputCardHasConveyableCard(cardData))
			{
				CardData inputCardConveyablePrefab = GetInputCardConveyablePrefab(cardData);
				string status = SokLoc.Translate("card_conveyor_status", LocParam.Create("resource", inputCardConveyablePrefab.Name));
				__instance.MyGameCard.StartTimer(__instance.TotalTime, __instance.LoadCard, status, __instance.GetActionId("LoadCard"));
				output = GetTarget(__instance, inputCardConveyablePrefab, -getDirectionVector(__instance), allowDraggedCards: true);
			}
			else
			{
				drag = null;
				__instance.MyGameCard.CancelAnyTimer();
			}
		}
		else if (drag is Boosterpack bp)
		{
			string status = SokLoc.Translate("card_conveyor_status", LocParam.Create("resource", bp.Name));
			__instance.MyGameCard.StartTimer(__instance.TotalTime, __instance.LoadCard, status, __instance.GetActionId("LoadCard"));
			output = GetTarget(__instance, (GameCard)null, -getDirectionVector(__instance), allowDraggedCards: true);
		}
		else
		{
			__instance.MyGameCard.CancelAnyTimer();
		}
		DrawArrows(__instance, getDirectionVector(__instance), drag, output);
		
		//((CardData)__instance).UpdateCard();
	
		//Log("11");
		return false;
	}

	public static Draggable GetInputDraggable(Conveyor conveyor, Vector3 directionVector, bool allowDraggingCards)
	{
		return GetBestInDirection(conveyor, directionVector, allowDraggingCards, (Draggable draggable) => CanBeInputDraggable(conveyor, draggable));
	}

	public static Draggable GetBestInDirection(Conveyor conveyor, Vector3 direction, bool allowDraggedCards, Func<Draggable, bool> pred)
	{
		Vector3 position = conveyor.MyGameCard.transform.position;
		float num = float.MinValue;
		Draggable result = null;
		float num2 = float.MaxValue;
		foreach (Draggable allDrag in WorldManager.instance.AllDraggables)
		{
			if (!allowDraggedCards && allDrag.BeingDragged)
			{
				continue;
			}
			Vector3 rhs = allDrag.transform.position - position;
			float num3 = Vector3.Dot(direction, rhs);
			if (num3 <= 0f)
			{
				continue;
			}
			float num4 = num3 / rhs.sqrMagnitude;
			if (num4 > 0.5f && num4 > num && pred(allDrag))
			{
				num = num4;
				Vector3 vector = allDrag.transform.position - position;
				vector.y = 0f;
				if (vector.magnitude <= 2f && vector.magnitude <= num2)
				{
					result = allDrag;
					num2 = vector.magnitude;
				}
			}
		}
		return result;
	}
	
	public static bool CanBeInputDraggable(Conveyor conveyor, Draggable drag)
	{
		if (drag is GameCard gc)
		{
			if (gc.HasCardInStack(cd => cd == conveyor))
			{
				return false;
			}
			CardData card = gc.GetLeafCard().CardData;

			if (card.MyGameCard.Velocity.HasValue || card.MyGameCard.BounceTarget != null)
			{
				return false;
			}
			if (conveyor.MyGameCard.IsParentOf(card.MyGameCard))
			{
				return false;
			}
			if (card is ResourceChest resourceChest)
			{
				if (string.IsNullOrEmpty(resourceChest.HeldCardId))
				{
					return false;
				}
				return CanBeConveyed(resourceChest.HeldCardId);
			}
			if (card is ResourceMagnet resourceMagnet)
			{
				if (string.IsNullOrEmpty(resourceMagnet.PullCardId))
				{
					return false;
				}
				return CanBeConveyed(resourceMagnet.PullCardId);
			}
			if (card is Chest chest)
			{
				if (string.IsNullOrEmpty(chest.HeldCardId))
				{
					return false;
				}
				return CanBeConveyed(chest.HeldCardId);
			}
			if (CanBeConveyed(card))
			{
				return true;
			}
		}
		else if (drag is Boosterpack)
		{
			return canUnloadBoosters.Value;
		}
		return false;
	}
	public static bool CanBeConveyed(string cardId)
	{
		CardData cardPrefab = WorldManager.instance.GetCardPrefab(cardId);
		return CanBeConveyed(cardPrefab);
	}
	public static bool CanBeConveyed(CardData otherCard)
	{
		if (otherCard == null)
		{
			return false;
		}
		switch (canMoveWhat.Value)
		{
			case WhatCardsCranable.All:
				break;
			case WhatCardsCranable.Draggable:
				if (!otherCard.CanBeDragged)
				{
					return false;
				}
				break;
			case WhatCardsCranable.DraggableButBuildings:
				if (!otherCard.CanBeDragged || otherCard.IsBuilding)
				{
					return false;
				}
				break;
			case WhatCardsCranable.SameAsBaseGame:
				if (otherCard.MyCardType != CardType.Resources
					&& otherCard.MyCardType != CardType.Food
					&& otherCard.MyCardType != CardType.Humans
					&& !(otherCard is Mob mob && !mob.IsAggressive))
				{
					return false;
				}
				break;

		}
		
		if (otherCard.MyGameCard == null)
		{
			return true;
		}
		
		GameCard statusCard = otherCard.MyGameCard.GetCardWithStatusInStack();
		if (statusCard != null && !canMoveOccupiedCards.Value)
		{
			if (statusCard.TimerBlueprintId != "")
			{
				Blueprint blueprintWithId = WorldManager.instance.GetBlueprintWithId(statusCard.TimerBlueprintId);
				Subprint subprint = blueprintWithId.Subprints[statusCard.TimerSubprintIndex];
				List<string> allCardsToRemove = subprint.GetAllCardsToRemove();
				List<GameCard> list = statusCard.GetAllCardsInStack();
				list.Remove(otherCard.MyGameCard);
				bool thereEnoughForPrint = true;
				for (int i = 0; i < allCardsToRemove.Count; i++)
				{
					string[] possibleRemovables = allCardsToRemove[i].Split('|');
					GameCard gameCard = list.FirstOrDefault((GameCard x) => possibleRemovables.Contains(x.CardData.Id));
					if (gameCard != null)
					{
						list.Remove(gameCard);
					}
					else
					{
						thereEnoughForPrint = false;
						break;
					}
				}
				if (!thereEnoughForPrint)
				{
					return false;
				}
			}
			else
			{
				return false;
			}
		}

		return true;
		/*if (otherCard is Conveyor)
		{
			return otherCard.CanBeDragged;
		}*/
	}

	public static bool InputCardHasConveyableCard(CardData card)
	{
		if (card is ResourceChest resourceChest)
		{
			return resourceChest.ResourceCount > 0;
		}
		if (card is ResourceMagnet resourceMagnet && resourceMagnet.MyGameCard.HasChild)
		{
			return true;
		}
		if (card is Chest chest && (chest.CoinCount > 0 || chest.MyGameCard.HasCardInStack(cd => cd is Chest ch && ch.CoinCount > 0)))
		{
			return true;
		}
		if (CanBeConveyed(card) || CanBeConveyed(card?.MyGameCard.GetLeafCard().CardData))
		{
			return true;
		}
		return false;
	}

	public static CardData GetInputCardConveyablePrefab(CardData card)
	{
		if (card is ResourceChest resourceChest)
		{
			return WorldManager.instance.GetCardPrefab(resourceChest.HeldCardId);
		}
		if (card is ResourceMagnet resourceMagnet)
		{
			return WorldManager.instance.GetCardPrefab(resourceMagnet.PullCardId);
		}
		if (card is Chest chest)
		{
			return WorldManager.instance.GetCardPrefab(chest.HeldCardId);
		}
		if (CanBeConveyed(card))
		{
			return card;
		}
		return null;
	}

	
	public static Draggable GetTarget(Conveyor conveyor, Draggable inputDrag, Vector3 direction, bool allowDraggedCards)
	{
		return GetBestInDirection(conveyor, direction, allowDraggedCards, delegate(Draggable drag)
		{
			if (drag == inputDrag)
			{
				return false;
			}
			if (drag is GameCard gc && gc.HasCardInStack(cd => cd == conveyor))
			{
				return false;
			}
			return OutputDraggableAllowed(drag, inputDrag);
		});
	}
	public static Draggable GetTarget(Conveyor conveyor, CardData inputPrefub, Vector3 direction, bool allowDraggedCards)
	{
		return GetBestInDirection(conveyor, direction, allowDraggedCards, delegate(Draggable drag)
		{
			if (drag is GameCard gc && (gc.CardData == inputPrefub || gc.HasCardInStack(cd => cd == conveyor)))
			{
				return false;
			}
			return OutputDraggableAllowed(drag, inputPrefub);
		});
	}
	public static bool OutputDraggableAllowed(Draggable drag, CardData inputPrefub)
	{
		
		if (drag is GameCard gc)
		{
			if(gc.HasCardInStack(cd => cd == inputPrefub))
			{
				return false;
			}
			GameCard realGC = gc.GetLeafCard();
			try
			{
				if (inputPrefub != null
					&& !realGC.CardData.CanHaveCardOnTop(inputPrefub, isPrefab: true)
					&& !(inputPrefub is Chest chest && realGC.CardData.Id == chest.HeldCardId))
				{
					return false;
				}
			}
			catch (Exception message)
			{
				if (Application.isEditor)
				{
					Debug.LogError(message);
				}
				return false;
			}
			return true;
		}
		else if (drag is CardTarget ct)
		{
			if (!canTargetSellBuy.Value)
			{
				return false;
			}
			return inputPrefub?.MyGameCard == null ? true : ct.CanHaveCard(inputPrefub.MyGameCard);
			//return false;
		}
		else if (drag is Boosterpack bp)
		{
			return false;
		}

		return false;
	}

	public static bool OutputDraggableAllowed(Draggable drag, Draggable inputDrag)
	{
		if (drag is GameCard gc)
		{			
			/*if (gc.CardData.Id == "heavy_foundation")
			{
				return true;
			}*/
			/*if (gc.HasChild)
			{
				return false;
			}*/
			if (!gc.gameObject.activeInHierarchy)
			{
				return false;
			}
			if (gc.MyBoard == null)
			{
				Debug.Log(gc?.ToString() + " does not have a board");
				return false;
			}
			if (!gc.MyBoard.IsCurrent)
			{
				return false;
			}
			try
			{
				if (inputDrag is GameCard inputGC && inputGC?.CardData != null)
				{
					return OutputDraggableAllowed(drag, inputGC.GetLeafCard().CardData);
				}
				else if (inputDrag is Boosterpack bp && canUnloadBoosters.Value)
				{
					return false;//CanBoosterHave(bp, gc);
				}
			}
			catch (Exception message)
			{
				if (Application.isEditor)
				{
					Debug.LogError(message);
				}
				return false;
			}
			return true;
		}
		else if (drag is CardTarget ct)
		{
			if (!canTargetSellBuy.Value)
			{
				return false;
			}
			if (inputDrag is GameCard inputGC)
			{
				return ct.CanHaveCard(inputGC.GetLeafCard());
			}
			return false;
		}
		else if (drag is Boosterpack bp)
		{
			CanBoosterHave(bp, inputDrag);
		}

		return false;
	}
	public static bool CanBoosterHave(Boosterpack bp, Draggable drag)
	{
		/*if(drag is GameCard gc)
		{
			return gc.CardData is Villager;
		}*/
		return false;
	}

	public static void DrawArrows(Conveyor conveyor, Vector3 directionVector, Draggable input, Draggable output)
	{
		DrawInputArrow(conveyor, directionVector, input);
		DrawOutputArrow(conveyor, directionVector, output);
	}
	
	public static Vector3 GetClosestCenter(Draggable drag, Conveyor conveyor)
	{
		if (drag is GameCard gameCard)
		{
			GameCard root = gameCard.GetRootCard();
			GameCard leaf = gameCard.GetLeafCard();
			Vector3 conveyorPosition = conveyor.transform.position;
			return new Vector3(Mathf.Clamp(conveyorPosition.x, Math.Min(root.transform.position.x, leaf.transform.position.x), Math.Max(root.transform.position.x, leaf.transform.position.x)),
				Mathf.Clamp(conveyorPosition.y, Math.Min(root.transform.position.y, leaf.transform.position.y), Math.Max(root.transform.position.y, leaf.transform.position.y)),
				Mathf.Clamp(conveyorPosition.z, Math.Min(root.transform.position.z, leaf.transform.position.z), Math.Max(root.transform.position.z, leaf.transform.position.z)));
		}
		else
		{
			return drag.transform.position;
		}
	}
	public static void DrawInputArrow(Conveyor conveyor, Vector3 directionVector, Draggable input)
	{
		Vector3 position = conveyor.MyGameCard.transform.position;
		Vector3 start = ((!(input != null)) ? (conveyor.MyGameCard.transform.position + directionVector * 0.5f) : TransformToEdge(conveyor, GetClosestCenter(input, conveyor), position, input, -1f));
		position = TransformToEdge(conveyor, start, position, conveyor.MyGameCard, 1f);
		DrawManager.instance.DrawShape(new ConveyorArrow
		{
			Start = start,
			End = position
		});
	}
	public static void DrawOutputArrow(Conveyor conveyor, Vector3 directionVector, Draggable output)
	{
		//Log(output != null ? output?.name : "no output");
		Vector3 position = conveyor.MyGameCard.transform.position;
		Vector3 end = output == null ? (conveyor.MyGameCard.transform.position - directionVector * 0.5f) : TransformToEdge(conveyor, position, GetClosestCenter(output, conveyor), output, 1f);
		position = TransformToEdge(conveyor, position, end, conveyor.MyGameCard, -1f);
		DrawManager.instance.DrawShape(new ConveyorArrow
		{
			Start = position,
			End = end
		});
	}
	
	public static Vector3 TransformToEdge(Conveyor conveyor, Vector3 start, Vector3 end, Draggable drag, float dir)
	{
		Vector2 start2 = new Vector2(start.x, start.z);
		Vector2 end2 = new Vector2(end.x, end.z);
		Vector2 pointOnCardEdge = GetPointOnDraggableEdge(start2, end2, drag);
		return new Vector3(pointOnCardEdge.x, 0f, pointOnCardEdge.y) + (start - end).normalized * conveyor.ExtraSideDistance * dir;
	}
	public static Vector2 GetPointOnDraggableEdge(Vector2 start, Vector2 end, Draggable drag)
	{
		drag.boxCollider.ToWorldSpaceBox(out var center, out var halfExtents, out var _);
		Bounds bounds = new Bounds(center, new Vector3(halfExtents.x * 2f, 0.01f, halfExtents.y * 2f));
		Vector2[] corners = new Vector2[4];
		corners[0] = new Vector2(bounds.min.x, bounds.min.z);
		corners[1] = new Vector2(bounds.max.x, bounds.min.z);
		corners[2] = new Vector2(bounds.max.x, bounds.max.z);
		corners[3] = new Vector2(bounds.min.x, bounds.max.z);
		for (int i = 0; i < 4; i++)
		{
			Vector2 p = corners[i];
			Vector2 p2 = corners[(i + 1) % 4];
			if (MathHelper.LineSegmentsIntersection(start, end, p, p2, out var intersection, out var _))
			{
				return intersection;
			}
		}
		return start;
	}
	
	[HarmonyPatch(typeof(Conveyor), "LoadCard")]
	[HarmonyPrefix]
	private static bool Conveyor_LoadCard_Prefix(ref Conveyor __instance)
	{
		Draggable input = GetInputDraggable(__instance, getDirectionVector(__instance), allowDraggingCards: false);
		if (input == null)
		{
			return false;
		}
		Draggable conveyableFromInput = GetConveyableFromInput(input);
		if (conveyableFromInput != null)
		{
			if (conveyableFromInput is GameCard gcConveyable)
			{
				gcConveyable.RemoveFromStack();
			}
			Draggable target = GetTarget(__instance, conveyableFromInput, -getDirectionVector(__instance), allowDraggedCards: false);
			if (target != null)
			{
				SendToTarget(conveyableFromInput, target);
			}
			else
			{
				conveyableFromInput.SendToPosition(__instance.MyGameCard.transform.position - getDirectionVector(__instance));
			}
		}
		return false;
	}
	public static Draggable GetConveyableFromInput(Draggable drag)
	{
		if (drag is GameCard gc)
		{
			CardData card = gc.GetLeafCard().CardData;
			if (card is ResourceChest resourceChest && resourceChest.ResourceCount > 0)
			{
				return resourceChest.RemoveResources(1);
			}
			if (card is ResourceMagnet resourceMagnet && resourceMagnet.MyGameCard.HasChild)
			{
				return resourceMagnet.MyGameCard.GetLeafCard();
			}
			if (card is Chest chest)
			{
				Chest chestWithCoins = chest;
				while (chestWithCoins.CoinCount == 0 && chestWithCoins.MyGameCard.Parent?.CardData is Chest parentChest)
				{
					chestWithCoins = parentChest;
				}
				chestWithCoins.CoinCount--;
				return WorldManager.instance.CreateCardStack(chestWithCoins.transform.position + Vector3.up * 0.2f, 1, chestWithCoins.HeldCardId, checkAddToStack: false);
			}
			if (CanBeConveyed(card))
			{
				return gc.GetLeafCard();
			}
			return null;
		}
		else if (drag is Boosterpack bp)
		{
			bp.Clicked();
			return WorldManager.instance.AllDraggables.FindLast(newDrag => newDrag != drag && Vector3.Distance(newDrag.transform.position, drag.transform.position) < 0.01f);
		}
		return null;
	}
	public static void SendToTarget(Draggable drag, Draggable target)
	{
		Draggable realTarget = target;
		if (target is GameCard targetGC)
		{
			realTarget = targetGC.GetLeafCard();
		}

		Vector3 vector = realTarget.transform.position - drag.transform.position;
		vector.y = 0f;
		Vector3 value = new Vector3(vector.x * 4f, 7f, vector.z * 4f);
		if (drag is GameCard gc)
		{
			if (realTarget is GameCard targetGC1)
			{
				gc.BounceTarget = targetGC1.GetRootCard();
			}
			else if (realTarget is CardTarget CardTarget)
			{
				CardTarget.CardDropped(gc);
			}
		}
		drag.Velocity = value;
	}

	
	[HarmonyPatch(typeof(DrawManager), "GetShapeDrawerForShape")]
	[HarmonyPrefix]
	private static void GetShapeDrawerForShape(IShape shape, ref DrawManager __instance, ref List<ShapeDrawer> ___shapeObjectPool, List<DrawManager.ShapeDrawerPrefab> ___Prefabs)
	{
		if (___shapeObjectPool.FirstOrDefault((ShapeDrawer o) => o.DrawingType == shape.GetType()) == null)
		{
            ShapeDrawer shapeDrawerPrefub = ___Prefabs.FirstOrDefault((DrawManager.ShapeDrawerPrefab o) => o.Prefab.DrawingType == shape.GetType())?.Prefab;
            if (shapeDrawerPrefub != null)
			{
				ShapeDrawer shapeDrawer = UnityEngine.Object.Instantiate(shapeDrawerPrefub);
				shapeDrawer.transform.SetParentClean(__instance.transform);
				shapeDrawer.gameObject.SetActive(value: false);
				___shapeObjectPool.Add(shapeDrawer);
			}
		}

	}
	
}
