//forward base setup
#ifndef JELLY_FORWARD_BASE_SETUP
	#define JELLY_FORWARD_BASE_SETUP

	#ifndef MK_GLASS_FWD_BASE_PASS
		#define MK_GLASS_FWD_BASE_PASS 1
	#endif

	#include "UnityGlobalIllumination.cginc"
	
	#include "UnityCG.cginc"
	#include "AutoLight.cginc"

	#include "../Common/JellyDef.cginc"
	#include "../Common/JellyV.cginc"
	#include "../Common/JellyInc.cginc"
	#include "../Forward/JellyForwardIO.cginc"
	#include "../Surface/JellySurfaceIO.cginc"
	#include "../Common/JellyLight.cginc"
	#include "../Surface/JellySurface.cginc"
#endif