// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AudioStream
{
    public abstract class AudioStreamBase : MonoBehaviour
    {
        // ========================================================================================================================================
        #region Required descendant's implementation
        protected abstract void StreamStarting(int samplerate);
        protected abstract bool StreamStarving();
        protected abstract void StreamPausing(bool pause);
        protected abstract void StreamStopping();
        public abstract void SetOutput(int outputDriverId);
        #endregion

        // ========================================================================================================================================
        #region Editor

        public enum StreamAudioType
        {
            MPEG,            /* MP2/MP3 MPEG. */
            OGGVORBIS,       /* Ogg vorbis. */
            WAV,             /* Microsoft WAV. */
            RAW,             /* Raw PCM data. */
        }

        [Header("[Source]")]

        [Tooltip("Audio stream - such as shoutcast/icecast - direct URL or m3u/8/pls playlist URL,\r\nor direct URL link to a single audio file.\r\n\r\nNOTE: it is possible to stream a local file. Pass complete file path WITHOUT the 'file://' prefix in that case. Stream type is ignored in that case.")]
        public string url = string.Empty;

        [Tooltip("Select proper type for net stream - otherwise streaming might not work especially on mobile. If inappropriate stream type is selected ( i.e. MPEG for OGG webradio ), streaming won't work and releasing sound ( i.e. starting new playback, or stopping play mode in the editor ) might cause instability of the whole streaming subsystem.")]
        public StreamAudioType streamType = StreamAudioType.MPEG;
        [Header("[RAW codec parameters]")]
        public FMOD.SOUND_FORMAT RAWSoundFormat = FMOD.SOUND_FORMAT.PCM16;
        public int RAWFrequency = 44000;
        public int RAWChannels = 2;

        [Header("[Setup]")]

        [Tooltip("Turn on/off logging to the Console. Errors are always printed.")]
        public LogLevel logLevel = LogLevel.ERROR;

        [Tooltip("When checked the stream will play on start. Otherwise use Play() method of this GameObject.")]
        public bool playOnStart = true;

        [Tooltip("Default is fine in most cases")]
        public FMOD.SPEAKERMODE speakerMode = FMOD.SPEAKERMODE.DEFAULT;

        #region Unity events
        [Header("[Events]")]
        public EventWithStringParameter OnPlaybackStarted;
        public EventWithStringBoolParameter OnPlaybackPaused;
        public EventWithStringParameter OnPlaybackStopped;
        public EventWithStringStringParameter OnError;
        #endregion

        int numOfRawSpeakers = 0;

        /// <summary>
        /// OAFR debug info
        /// </summary>
        string gameObjectName = string.Empty;
        #endregion

        // ========================================================================================================================================
        #region Init && FMOD structures
        /// <summary>
        /// Component startup sync
        /// </summary>
        [HideInInspector]
        public bool ready = false;

        protected FMOD.System system = null;
        protected FMOD.Sound sound = null;
        protected FMOD.Channel channel = null;
        protected FMOD.RESULT result = FMOD.RESULT.OK;
        protected FMOD.OPENSTATE openstate = FMOD.OPENSTATE.READY;
        uint version = 0;
        System.IntPtr extradriverdata = System.IntPtr.Zero;

        FMOD.RESULT lastError = FMOD.RESULT.OK;
        const int streamBufferSize = 64 * 1024;

        protected virtual IEnumerator Start()
        {
            this.gameObjectName = this.gameObject.name;

            /*
                Create a System object and initialize.
            */

            result = FMOD.Factory.System_Create(out system);
            ERRCHECK(result, "Factory.System_Create");

            result = system.getVersion(out version);
            ERRCHECK(result, "system.getVersion");

            if (version < FMOD.VERSION.number)
            {
                var msg = string.Format("FMOD lib version {0} doesn't match header version {1}", version, FMOD.VERSION.number);

                LOG(LogLevel.ERROR, msg);

                if (this.OnError != null)
                    this.OnError.Invoke(this.gameObjectName, msg);

                throw new System.Exception(msg);
            }

            /*
             * initial internal FMOD samplerate should be 48000 on desktop; we change it on the sound only when stream requests it.
             */
            result = system.setSoftwareFormat(48000, this.speakerMode, this.numOfRawSpeakers);
            ERRCHECK(result, "system.setSoftwareFormat");

            int rate;
            FMOD.SPEAKERMODE sm;
            int smch;

            result = system.getSoftwareFormat(out rate, out sm, out smch);
            ERRCHECK(result, "system.getSoftwareFormat");

            LOG(LogLevel.INFO, "FMOD samplerate: {0}, speaker mode: {1}, num. of raw speakers {2}", rate, sm, smch);

            // must be be4 init on iOS ...
            //result = system.setOutput(FMOD.OUTPUTTYPE.NOSOUND);
            //ERRCHECK(result, "system.setOutput");

            if (this is AudioStreamMinimal)
                result = system.init(8, FMOD.INITFLAGS.NORMAL, extradriverdata);
            else
                result = system.init(8, FMOD.INITFLAGS.STREAM_FROM_UPDATE, extradriverdata);
            ERRCHECK(result, "system.init");


            /* Increase the file buffer size a little bit to account for Internet lag. */
            result = system.setStreamBufferSize(streamBufferSize, FMOD.TIMEUNIT.RAWBYTES);
            ERRCHECK(result, "system.setStreamBufferSize");

            /* tags ERR_FILE_COULDNOTSEEK:
                http://stackoverflow.com/questions/7154223/streaming-mp3-from-internet-with-fmod
                http://www.fmod.org/docs/content/generated/FMOD_System_SetFileSystem.html
                */
            result = system.setFileSystem(null, null, null, null, null, null, -1);
            ERRCHECK(result, "system.setFileSystem");

            if (this.playOnStart)
                this.Play();

            yield return null;

            this.ready = true;
        }

        #endregion

        // ========================================================================================================================================
        #region Playback
        [Header("[Playback info]")]
        [Range(0f, 100f)]
        [Tooltip("Set during playback. Stream buffer fullness")]
        public uint bufferFillPercentage = 0;
        [Tooltip("Set during playback.")]
        public bool isPlaying = false;
        [Tooltip("Set during playback.")]
        public bool isPaused = false;
        /// <summary>
        /// (starving is meaningless without playSound..)
        /// </summary>
        [Tooltip("Set during playback.")]
        public bool starving = false;
        [Tooltip("Set during playback when stream is refreshing data.")]
        public bool deviceBusy = false;
        [Tooltip("Radio station title. Set from PLS playlist.")]
        public string title;
        [Tooltip("Set during playback.")]
        public int streamChannels;
        const int tagcount = 4;
        int tagindex = 0;
        [Tooltip("Tags supplied by the stream. Varies heavily from stream to stream")]
        public string[] tags = new string[tagcount];
        /// <summary>
        /// Stream interrupted / file finished, i.e. stream stopped without user interaction.
        /// </summary>
        protected bool finished = false;

        public void Play()
        {
            if (this.isPlaying)
            {
                LOG(LogLevel.WARNING, "Already playing.");
                return;
            }

            if (!this.isActiveAndEnabled)
            {
                LOG(LogLevel.WARNING, "Will not start on disabled GameObject.");
                return;
            }

            /*
             * url format check
             */
            if (string.IsNullOrEmpty(this.url))
            {
                var msg = "Can't stream empty URL";

                LOG(LogLevel.ERROR, msg);

                if (this.OnError != null)
                    this.OnError.Invoke(this.gameObjectName, msg);

                throw new System.Exception(msg);
            }

            this.tags = new string[tagcount];

            this.isPlaying = false;
            this.isPaused = false;
            this.finished = false;

            this.Stop_Internal(); // try to clean partially started playback

            StartCoroutine(this.PlayCR());
        }

        enum PlaylistType
        {
            PLS
                , M3U
                , M3U8
        }

        PlaylistType? playlistType;

        IEnumerator PlayCR()
        {
            var _url = this.url;

            this.playlistType = null;

            if (this.url.EndsWith("pls", System.StringComparison.OrdinalIgnoreCase))
                this.playlistType = PlaylistType.PLS;
            else if (this.url.EndsWith("m3u", System.StringComparison.OrdinalIgnoreCase))
                this.playlistType = PlaylistType.M3U;
            else if (this.url.EndsWith("m3u8", System.StringComparison.OrdinalIgnoreCase))
                this.playlistType = PlaylistType.M3U8;

            if (this.playlistType.HasValue)
            {
                string playlist = string.Empty;

                // allow local playlist
                if (!this.url.StartsWith("http", System.StringComparison.OrdinalIgnoreCase) && !this.url.StartsWith("file", System.StringComparison.OrdinalIgnoreCase))
                    this.url = "file://" + this.url;

                /*
                 * UnityWebRequest introduced in 5.2, but WWW still worked on standalone/mobile
                 * However, in 5.3 is WWW hardcoded to Abort() on iOS on non secure requests - which is likely a bug - so from 5.3 on we require UnityWebRequest
                 */
#if UNITY_5_3_OR_NEWER
#if UNITY_5_3
                using (UnityEngine.Experimental.Networking.UnityWebRequest www = UnityEngine.Experimental.Networking.UnityWebRequest.Get(this.url))
#else
                using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequest.Get(this.url))
#endif
                {
                    yield return www.Send();

                    if (www.isError || !string.IsNullOrEmpty(www.error))
                    {
                        var msg = string.Format("Can't read playlist from {0} - {1}", this.url, www.error);

                        LOG(LogLevel.ERROR, msg);

                        if (this.OnError != null)
                            this.OnError.Invoke(this.gameObjectName, msg);

                        throw new System.Exception(msg);
                    }

                    playlist = www.downloadHandler.text;
                }
#else
                using (WWW www = new WWW(this.url))
                {
                    yield return www;

                    if (!string.IsNullOrEmpty(www.error))
                    {
                        var msg = string.Format("Can't read playlist from {0} - {1}", this.url, www.error);

                        LOG(LogLevel.ERROR, msg);
                
                        if (this.OnError != null)
                            this.OnError.Invoke(this.gameObjectName, msg);

                        throw new System.Exception(msg);
                    }

                    playlist = www.text;
                }
#endif

                if (this.playlistType.Value == PlaylistType.M3U
                || this.playlistType.Value == PlaylistType.M3U8)
                {
                    _url = this.URLFromM3UPlaylist(playlist);
                    LOG(LogLevel.INFO, "URL from M3U/8 playlist: {0}", _url);
                }
                else
                {
                    _url = this.URLFromPLSPlaylist(playlist);
                    LOG(LogLevel.INFO, "URL from PLS playlist: {0}", _url);
                }

                if (string.IsNullOrEmpty(_url))
                {
                    var msg = string.Format("Can't parse playlist {0}", this.url);

                    LOG(LogLevel.ERROR, msg);

                    if (this.OnError != null)
                        this.OnError.Invoke(this.gameObjectName, msg);

                    throw new System.Exception(msg);
                }

                // allow FMOD to stream locally
                if (_url.StartsWith("file://", System.StringComparison.OrdinalIgnoreCase))
                    _url = _url.Substring(7);
            }

            /*
             * pass empty / default CREATESOUNDEXINFO, otherwise it hits nomarshalable unmanaged structure path on IL2CPP 
             */
            var extInfo = new FMOD.CREATESOUNDEXINFO();
            // must be hinted on iOS due to ERR_FILE_COULDNOTSEEK on getOpenState
            // allow any type for local files
            switch (this.streamType)
            {
                case StreamAudioType.MPEG:
                    extInfo.suggestedsoundtype = FMOD.SOUND_TYPE.MPEG;
                    break;
                case StreamAudioType.OGGVORBIS:
                    extInfo.suggestedsoundtype = FMOD.SOUND_TYPE.OGGVORBIS;
                    break;
                case StreamAudioType.WAV:
                    extInfo.suggestedsoundtype = FMOD.SOUND_TYPE.WAV;
                    break;
                case StreamAudioType.RAW:
                    extInfo.suggestedsoundtype = FMOD.SOUND_TYPE.RAW;
                    break;
                default:
                    extInfo.suggestedsoundtype = FMOD.SOUND_TYPE.UNKNOWN;
                    break;
            }

            /*
             * opening flags for streaming createSound
             */
            var flags = FMOD.MODE.CREATESTREAM
                | FMOD.MODE.NONBLOCKING
                | FMOD.MODE.IGNORETAGS
                | FMOD.MODE.MPEGSEARCH
                | FMOD.MODE.OPENONLY
                ;

            if (this.streamType == StreamAudioType.RAW)
            {
                // raw data needs to ignore audio format and
                // Use FMOD_CREATESOUNDEXINFO to specify format.Requires at least defaultfrequency, numchannels and format to be specified before it will open.Must be little endian data.

                flags |= FMOD.MODE.OPENRAW;

                extInfo.format = this.RAWSoundFormat;
                extInfo.defaultfrequency = this.RAWFrequency;
                extInfo.numchannels = this.RAWChannels;
            }

            result = system.createSound(_url
                , flags
                , ref extInfo
                , out sound);
            ERRCHECK(result, "system.createSound");


			if (Application.platform == RuntimePlatform.IPhonePlayer)
			{
				LOG (LogLevel.INFO, "Setting playback output to speaker...");
			    //iOSSpeaker.RouteForPlayback();
           iOSSpeaker.RouteToSpeaker();
            }

            LOG(LogLevel.INFO, "About to play...");

            StartCoroutine(this.StreamCR());
        }

        /// <summary>
        /// Stream caught flag.
        /// </summary>
        bool streamCaught = false;
        /// <summary>
        /// On track change update tags properly
        /// , TODO: track change event
        /// </summary>
        bool trackChanged = false;

        IEnumerator StreamCR()
        {
            /*
             * FMOD seems to like this
             */
            yield return new WaitForSeconds(2f);

            this.streamCaught = false;
            this.trackChanged = false;

            for (;;)
            {
                if (this.isPaused)
                    yield return null;

                if (this.finished)
                {
                    this.Stop();
                    yield break;
                }

                result = system.update();
                ERRCHECK(result, "system.update", false);

                result = sound.getOpenState(out openstate, out bufferFillPercentage, out starving, out deviceBusy);
                ERRCHECK(result, "sound.getOpenState", false);

                LOG(LogLevel.DEBUG, "Stream open state: {0}, buffer fill {1} starving {2} networkBusy {3}", openstate, bufferFillPercentage, starving, deviceBusy);

                if (!this.streamCaught)
                {
                    int c = 0;
                    do
                    {
                        result = system.update();
                        ERRCHECK(result, "system.update", false);

                        result = sound.getOpenState(out openstate, out bufferFillPercentage, out starving, out deviceBusy);
                        ERRCHECK(result, "sound.getOpenState", false);

                        LOG(LogLevel.DEBUG, "Stream open state: {0}, buffer fill {1} starving {2} networkBusy {3}", openstate, bufferFillPercentage, starving, deviceBusy);

                        if (result == FMOD.RESULT.OK && openstate == FMOD.OPENSTATE.READY)
                        {
                            /*
                             * stream caught
                             */
                            FMOD.SOUND_TYPE _streamType;
                            FMOD.SOUND_FORMAT _streamFormat;
                            int _streamBits;

                            result = sound.getFormat(out _streamType, out _streamFormat, out this.streamChannels, out _streamBits);
                            ERRCHECK(result, "sound.getFormat");

                            float freq; int prio;
                            result = sound.getDefaults(out freq, out prio);
                            ERRCHECK(result, "sound.getDefaults");

                            LOG(LogLevel.INFO, "Stream format {0} {1} {2} channels {3} bits {4} samplerate", _streamType, _streamFormat, this.streamChannels, _streamBits, freq);

                            if (this is AudioStream)
							{
								// compensate for the stream samplerate
								this.GetComponent<AudioSource>().pitch = ((float)(freq * this.streamChannels) / (float)(AudioSettings.outputSampleRate * (int)AudioSettings.speakerMode));
							}

                            this.StreamStarting((int)freq);

                            this.streamCaught = true;

                            this.isPlaying = true;

                            if (this.OnPlaybackStarted != null)
                                this.OnPlaybackStarted.Invoke(this.gameObjectName);

                            break;
                        }
                        else
                        {
                            /*
                             * Unable to stream
                             */
                            if (++c > 60)
                            {
                                if (this.url.StartsWith("http"))
                                {
                                    LOG(LogLevel.ERROR, "Can't start playback. Please make sure that correct audio type of stream is selected and network is reachable.");
#if UNITY_EDITOR
                                    LOG(LogLevel.ERROR, "If everything seems to be ok, restarting the editor often helps while having trouble connecting to especially OGG streams.");
#endif
                                }
                                else
                                {
                                    LOG(LogLevel.ERROR, "Can't start playback. Unrecognized audio type.");
                                }

                                this.Stop();

                                yield break;
                            }
                        }

                        yield return new WaitForSeconds(0.1f);

                    } while (result != FMOD.RESULT.OK || openstate != FMOD.OPENSTATE.READY);
                }

                if (this.StreamStarving())
                {
                    LOG(LogLevel.WARNING, "Stream buffer starving - stopping playback");

                    this.Stop();

                    yield break;
                }

                FMOD.TAG streamTag;
                /*
                    Read any tags that have arrived, this could happen if a radio station switches
                    to a new song.
                */
                while (sound.getTag(null, -1, out streamTag) == FMOD.RESULT.OK)
                {
                    if ( !this.trackChanged )
                    {
                        this.tags = new string[tagcount];
                        this.tagindex = 0;
                        this.trackChanged = true;
                    }

                    if (streamTag.datatype == FMOD.TAGDATATYPE.STRING)
                    {
                        string tagData = Marshal.PtrToStringAnsi(streamTag.data, (int)streamTag.datalen);
                        tags[tagindex] = string.Format("{0} = {1}", streamTag.name, tagData);
                        tagindex = (tagindex + 1) % tagcount;
                    }
                    else if (streamTag.type == FMOD.TAGTYPE.FMOD)
                    {
                        /* When a song changes, the samplerate may also change, so compensate here. */
                        if (streamTag.name == "Sample Rate Change")
                        {
                            // TODO: actual float and samplerate change test - is there a way to test this ?

                            float defFrequency;
                            int defPriority;
                            result = sound.getDefaults(out defFrequency, out defPriority);
                            ERRCHECK(result, "sound.getDefaults");

                            LOG(LogLevel.INFO, "Stream samplerate change from {0}", defFrequency);

                            // float frequency = *((float*)streamTag.data);
                            float[] frequency = new float[1];
                            Marshal.Copy(streamTag.data, frequency, 0, sizeof(float));

                            LOG(LogLevel.INFO, "Stream samplerate change to {0}", frequency[0]);

                            result = sound.setDefaults(frequency[0], defPriority);
                            ERRCHECK(result, "sound.setDefaults");

                            /*
                             * need to restart audio when changing samplerate..
                             */
							if (this is AudioStream)
							{
								// compensate for the stream samplerate
								this.GetComponent<AudioSource>().pitch = ((float)(frequency[0] * this.streamChannels) / (float)(AudioSettings.outputSampleRate * (int)AudioSettings.speakerMode));
							}
                        }
                    }
                }

                if ( this.trackChanged )
                {
                    // TODO: track changed event
                    LOG(LogLevel.INFO, "Track changed {0}", this.tags[0]); // print tags[0] for info only - might be anything
                    this.trackChanged = false;
                }

                yield return null;
            }
        }

        public void Pause(bool pause)
        {
            if (!this.isPlaying)
            {
                LOG(LogLevel.WARNING, "Not playing..");
                return;
            }

            this.StreamPausing(pause);

            this.isPaused = pause;

            LOG(LogLevel.INFO, "{0}", this.isPaused ? "paused." : "resumed.");

            if (this.OnPlaybackPaused != null)
                this.OnPlaybackPaused.Invoke(this.gameObjectName, this.isPaused);
        }

        #endregion

        // ========================================================================================================================================
        #region Shutdown
        /// <summary>
        /// wrong combination of requested audio type and actual stream type leads to still BUFFERING/LOADING state of the stream
        /// don't release sound and system in that case and notify user
        /// </summary>
        bool unstableShutdown = false;

        public void Stop()
        {
            if (!this.isPlaying)
                return;

            LOG(LogLevel.INFO, "Stopping..");

            this.StreamStopping();

            this.StopAllCoroutines();

            this.Stop_Internal();

            if (this.OnPlaybackStopped != null)
                this.OnPlaybackStopped.Invoke(this.gameObjectName);
        }

        /// <summary>
        /// Stop and try to release FMOD sound resources
        /// </summary>
        void Stop_Internal()
        {
            this.bufferFillPercentage = 0;
            this.isPlaying = false;
            this.isPaused = false;
            this.finished = false;
            this.starving = false;
            this.deviceBusy = false;
            this.tags = new string[tagcount];

            /*
                Stop the channel, then wait for it to finish opening before we release it.
            */
            if (channel != null)
            {
                result = channel.stop();
                // ERRCHECK(result, "channel.stop", false);
            }

            channel = null;

            /*
             * on wrong requested audio type getOpenState is still returning BUFFERING/LOADING here 
             * it is not possible to release sound and system due to FMOD deadlocking
             */
            this.unstableShutdown = false;

            LOG(LogLevel.DEBUG, "Waiting for sound to finish opening before trying to release it....");

            System.Threading.Thread.Sleep(50);

            int c = 0;
            do
            {
                if (++c > 5)
                    break;

                System.Threading.Thread.Sleep(10);

                result = FMOD.RESULT.OK;
                openstate = FMOD.OPENSTATE.READY;

                if (system != null)
                {
                    result = system.update();
                    // ERRCHECK(result, "system.update", false);
                }

                if (sound != null)
                {
                    result = sound.getOpenState(out openstate, out bufferFillPercentage, out starving, out deviceBusy);
                    // ERRCHECK(result, "sound.getOpenState", false);

                    LOG(LogLevel.DEBUG, "Stream open state: {0}, buffer fill {1} starving {2} networkBusy {3}", openstate, bufferFillPercentage, starving, deviceBusy);
                }
            }
            while (openstate != FMOD.OPENSTATE.READY || result != FMOD.RESULT.OK);

            if (openstate == FMOD.OPENSTATE.BUFFERING)
            {
                if (result != FMOD.RESULT.ERR_NET_URL)
                {
                    this.unstableShutdown = true;
                    LOG(LogLevel.ERROR, "AudioStreamer is in unstable state - please restart editor/application. [{0} {1}]", openstate, result);
                }
            }


            /*
                Shut down
            */

            if (sound != null && !this.unstableShutdown)
            {
                result = sound.release();
                // ERRCHECK(result, "sound.release", false);
            }

            sound = null;
        }

        protected virtual void OnDisable()
        {
            this.Stop();

            if (system != null && !this.unstableShutdown)
            {
                result = system.close();
                // ERRCHECK(result, "system.close", false);

                result = system.release();
                // ERRCHECK(result, "system.release", false);
            }

            system = null;
        }
        #endregion

        // ========================================================================================================================================
        #region Support

        protected void ERRCHECK(FMOD.RESULT result, string customMessage, bool throwOnError = true)
        {
            this.lastError = result;

            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, this.OnError, customMessage, throwOnError);
        }

        protected void LOG(LogLevel requestedLogLevel, string format, params object[] args)
        {
            AudioStreamSupport.LOG(requestedLogLevel, this.logLevel, this.gameObjectName, this.OnError, format, args);
        }

        public string GetLastError(out FMOD.RESULT errorCode)
        {
            errorCode = this.lastError;
            return FMOD.Error.String(errorCode);
        }

        /// <summary>
        /// M3U/8 = its own simple format: https://en.wikipedia.org/wiki/M3U
        /// </summary>
        /// <param name="_playlist"></param>
        /// <returns></returns>
        string URLFromM3UPlaylist(string _playlist)
        {
            System.IO.StringReader source = new System.IO.StringReader(_playlist);

            string s = source.ReadLine();
            while (s != null)
            {
                // If the read line isn't a metadata, it's a file path
                if ((s.Length > 0) && (s[0] != '#'))
                    return s;

                s = source.ReadLine();
            }

            return null;
        }

        /// <summary>
        /// PLS ~~ INI format: https://en.wikipedia.org/wiki/PLS_(file_format)
        /// </summary>
        /// <param name="_playlist"></param>
        /// <returns></returns>
        string URLFromPLSPlaylist(string _playlist)
        {
            System.IO.StringReader source = new System.IO.StringReader(_playlist);

            string s = source.ReadLine();

            int equalIndex;
            while (s != null)
            {
                if (s.Length > 4)
                {
                    // If the read line isn't a metadata, it's a file path
                    if ("FILE" == s.Substring(0, 4).ToUpper())
                    {
                        equalIndex = s.IndexOf("=") + 1;
                        s = s.Substring(equalIndex, s.Length - equalIndex);

                        return s;
                    }
                }

                s = source.ReadLine();
            }

            return null;
        }
        #endregion

        // ========================================================================================================================================
        #region User support
        /// <summary>
        /// Enumerates available audio outputs in the system and returns their names.
        /// </summary>
        /// <returns></returns>
        public List<string> AvailableOutputs()
        {
            List<string> availableDriversNames = new List<string>();

            int numDrivers;
            result = system.getNumDrivers(out numDrivers);
            ERRCHECK(result, "system.getNumDrivers");

            for (int i = 0; i < numDrivers; ++i)
            {
                int namelen = 255;
                System.Text.StringBuilder name = new System.Text.StringBuilder(namelen);
                System.Guid guid;
                int systemrate;
                FMOD.SPEAKERMODE speakermode;
                int speakermodechannels;

                result = system.getDriverInfo(i, name, namelen, out guid, out systemrate, out speakermode, out speakermodechannels);
                ERRCHECK(result, "system.getDriverInfo");

                availableDriversNames.Add(name.ToString());

                LOG(LogLevel.DEBUG, "{0}{1}guid: {2}{3}systemrate: {4}{5}speaker mode: {6}{7}channels: {8}"
                    , name
                    , System.Environment.NewLine
                    , guid
                    , System.Environment.NewLine
                    , systemrate
                    , System.Environment.NewLine
                    , speakermode
                    , System.Environment.NewLine
                    , speakermodechannels
                    );
            }

            return availableDriversNames ;
        }

        #endregion
    }
}