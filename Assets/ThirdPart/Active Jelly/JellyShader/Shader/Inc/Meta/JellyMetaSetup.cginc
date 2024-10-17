//Meta setup
#ifndef MK_GLASS_META_SETUP
	#define MK_GLASS_META_SETUP

	#ifndef _EMISSION
		#define _EMISSION 1
	#endif

	#ifndef MK_GLASS_META_PASS
		#define MK_GLASS_META_PASS 1
	#endif

	#include "UnityCG.cginc"
	#include "../Common/JellyDef.cginc"
	#ifndef MKGLASS_TC
		#define MKGLASS_TC 1
	#endif
	#include "../Common/JellyV.cginc"
	#include "../Common/JellyInc.cginc"
	#include "../Surface/JellySurfaceIO.cginc"
	#include "../Surface/JellySurface.cginc"
	#include "JellyMetaIO.cginc"
#endif