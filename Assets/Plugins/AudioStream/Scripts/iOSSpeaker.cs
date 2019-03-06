// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

using UnityEngine;
using System.Runtime.InteropServices;

public static class iOSSpeaker
{
#if UNITY_IPHONE
	[DllImport("__Internal")]
	private static extern void _RouteToSpeaker();
	[DllImport("__Internal")]
    private static extern void _RouteNormal();
#endif

    public static void RouteToSpeaker()
    {
#if UNITY_IPHONE
		if (Application.platform == RuntimePlatform.IPhonePlayer)
			_RouteToSpeaker();
#endif
    }

    public static void RouteNormal()
    {
#if UNITY_IPHONE
		if (Application.platform == RuntimePlatform.IPhonePlayer)
			_RouteNormal();
#endif
    }
}
