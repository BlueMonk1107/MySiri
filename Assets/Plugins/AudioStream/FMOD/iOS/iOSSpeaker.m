// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

#import "iOSSpeaker.h"
#import <AVFoundation/AVFoundation.h>

void _RouteToSpeaker()
{
    // if the headset is connected leave routing as is, i.e. output to headset
    if (_headsetConnected())
        return;
    
    OSStatus error;
    UInt32 audioRouteOverride = kAudioSessionOverrideAudioRoute_Speaker;
    error = AudioSessionSetProperty(kAudioSessionProperty_OverrideAudioRoute,
                                     sizeof(audioRouteOverride),
                                     &audioRouteOverride);
    
    if (error)
        NSLog(@"Audio already seems to be playing through speaker");
    else
        NSLog(@"Forcing audio to speaker");
}

void _RouteNormal()
{
    if (_headsetConnected())
        return;
    
    OSStatus error;
    UInt32 audioRouteOverride = kAudioSessionOverrideAudioRoute_None;
    error = AudioSessionSetProperty(kAudioSessionProperty_OverrideAudioRoute,
                                     sizeof(audioRouteOverride),
                                     &audioRouteOverride);
    
    if (error)
        NSLog(@"Audio already seems to be playing normally");
    else
        NSLog(@"Forcing audio to normal");
}

bool _headsetConnected()
{
    UInt32 routeSize = sizeof(CFStringRef);
    CFStringRef route = NULL;
    OSStatus error = AudioSessionGetProperty(kAudioSessionProperty_AudioRoute, &routeSize, &route);
    
    if (!error &&
        (route != NULL)&&
        ([(__bridge NSString*)route rangeOfString:@"Head"].location != NSNotFound))
    {
        /*  don't think this is needed
            see "the get rule":
            https://developer.apple.com/library/mac/#documentation/CoreFoundation/Conceptual/CFMemoryMgmt/Concepts/Ownership.html#//apple_ref/doc/uid/20001148-CJBEJBHH
        */
        //CFRelease(route);
        
        NSLog(@"Headset connected");
        return true;
    }
    
    return false;
}
