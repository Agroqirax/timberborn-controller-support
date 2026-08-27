using System;
using HarmonyLib;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace ControllerSupport
{
	public class ControllerSupportModStarter : IModStarter
	{
		public void StartMod(IModEnvironment modEnvironment)
		{
			var harmony = new Harmony("Agroqirax.ControllerSupport");
			harmony.PatchAll();

			try
			{
				FPPCameraIntegration.TryApply(harmony);
			}
			catch (Exception e)
			{
				Debug.LogError($"[ControllerSupport] FPPCamera integration failed to start: {e}");
			}
		}
	}
}
