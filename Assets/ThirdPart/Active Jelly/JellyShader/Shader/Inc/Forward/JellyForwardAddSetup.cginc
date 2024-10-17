//forward add setup
#ifndef JELLY_FORWARD_ADD_SETUP
	#define MK_GLASS_FORWARD_ADD_SETUP

	#ifndef JELLY_FWD_ADD_PASS
		#define MK_GLASS_FWD_ADD_PASS 1 
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