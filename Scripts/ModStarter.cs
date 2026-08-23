using HarmonyLib;
using Timberborn.ModManagerScene;

namespace ControllerSupport
{
	public class ControllerSupportModStarter : IModStarter
	{
		public void StartMod(IModEnvironment modEnvironment)
		{
			new Harmony("Agroqirax.ControllerSupport").PatchAll();
		}
	}
}
