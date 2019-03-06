  
using AudioStream;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode()]
public class AudioStreamDemo : MonoBehaviour
{
    public AudioStreamBase[] audioStreams;

    #region UI events

    Dictionary<string, string> streamsStatesFromEvents = new Dictionary<string, string>();

    public void OnPlaybackStarted(string goName)
    {
        this.streamsStatesFromEvents[goName] = "playing";
    }

    public void OnPlaybackPaused(string goName, bool paused)
    {
        this.streamsStatesFromEvents[goName] = paused ? "paused" : "playing";
    }

    public void OnPlaybackStopped(string goName)
    {
        this.streamsStatesFromEvents[goName] = "stopped";
    }

    public void OnError(string goName, string msg)
    {
        this.streamsStatesFromEvents[goName] = msg;
    }

    #endregion
 
    int dpiMult = 1;

    void Start()
    {
        if (Screen.dpi > 300) // ~~ retina
            this.dpiMult = 2;
    }




    public void OnPlay()
    {
        if (audioStreams[0].isPlaying)
        {
            audioStreams[0].Stop(); 
        }
        else
        {
            audioStreams[0].Play();
        }
    }
    public void SetUrlContext(string url)
    {
        audioStreams[0].url = url;
    }
}
