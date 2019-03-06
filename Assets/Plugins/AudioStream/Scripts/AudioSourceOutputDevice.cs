// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

using System;
using System.Collections.Generic;
using UnityEngine;

namespace AudioStream
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioSourceOutputDevice : MonoBehaviour
    {
        // ========================================================================================================================================
        #region Editor
        [Header("Setup")]
        [Tooltip("You can specify any available audio output device present in the system.\r\nPass an interger number between 0 and 'getNumDrivers' - see demo scene's Start() and AvailableOutputs()")]
        public int outputDriverID = 0;
        [Tooltip("When used with streaming we have to obtain sample rate from the actual stream, therefore start only when the stream is ready. Set this to false when used in conjuction with AudioStream component, or when changing output driver at runtime. When set to true you'd also have to stop redirection - i.e. calling this StopRedirect method - by yourself.")]
        public bool autoStart = false;

        public LogLevel logLevel = LogLevel.ERROR;

        string gameObjectName = string.Empty;
        #endregion

        // ========================================================================================================================================
        #region FMOD && Unity audio callback
        protected FMOD.System system = null;
        protected FMOD.Sound sound = null;
        protected FMOD.Channel channel = null;
        protected FMOD.RESULT result = FMOD.RESULT.OK;
        uint version = 0;
        System.IntPtr extradriverdata = System.IntPtr.Zero;

        FMOD.SOUND_PCMREADCALLBACK pcmreadcallback;
        FMOD.SOUND_PCMSETPOSCALLBACK pcmsetposcallback;

        /// <summary>
        /// the size of a single sample ( i.e. per channel ) is the size of Int16 a.k.a. signed short 
        /// specified by FMOD.SOUND_FORMAT.PCM16 format while creating sound info for createSound
        /// </summary>
        int elementSize;
        /// <summary>
        /// OAFR buffer size - a necessary precondition for redirect to start
        /// </summary>
        int unityOAFRDataLength;
        /// <summary>
        /// OAFR channels - a necessary precondition for redirect to start
        /// </summary>
        int unityOAFRChannels;
        /// <summary>
        /// data && channels : startup / change
        /// </summary>
        bool oafrDataFormatChanged = false;
        #endregion

        // ========================================================================================================================================
        #region Unity lifecycle
        void Start()
        {
            this.gameObjectName = this.gameObject.name;


            result = FMOD.Factory.System_Create(out system);
            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, null, "FMOD.Factory.System_Create");

            result = system.getVersion(out version);
            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, null, "system.getVersion");

            if (version < FMOD.VERSION.number)
            {
                var msg = string.Format("FMOD lib version {0} doesn't match header version {1}", version, FMOD.VERSION.number);
                throw new System.Exception(msg);
            }

            int rate;
            FMOD.SPEAKERMODE sm;
            int sc;

            result = system.getSoftwareFormat(out rate, out sm, out sc);
            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, null, "system.getSoftwareFormat");

            AudioStreamSupport.LOG(LogLevel.INFO, this.logLevel, this.gameObjectName, null, "FMOD samplerate: {0}, speaker mode: {1}, num. of raw speakers {2}", rate, sm, sc);

            // TODO: evaluate maxchannels
            result = system.init(32, FMOD.INITFLAGS.NORMAL, extradriverdata);
            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, null, "system.init");


            this.SetOutput(this.outputDriverID);

            /* tags ERR_FILE_COULDNOTSEEK:
                    http://stackoverflow.com/questions/7154223/streaming-mp3-from-internet-with-fmod
                    http://www.fmod.org/docs/content/generated/FMOD_System_SetFileSystem.html
                    */
            // result = system.setFileSystem(null, null, null, null, null, null, -1);
            // ERRCHECK(result, "system.setFileSystem");

            // Explicitly create the delegate object and assign it to a member so it doesn't get freed
            // by the garbage collected while it's being used
            this.pcmreadcallback = new FMOD.SOUND_PCMREADCALLBACK(PCMReadCallback);
            this.pcmsetposcallback = new FMOD.SOUND_PCMSETPOSCALLBACK(PCMSetPosCallback);


            this.elementSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(System.Int16));


            // decodebuffersize samples worth of bytes will be called in read callback
            // createSound calls back, too
            this.pcmReadCallbackBuffer = new List<List<byte>>();
            this.pcmReadCallbackBuffer.Add(new List<byte>());
            this.pcmReadCallbackBuffer.Add(new List<byte>());
        }

        void Update()
        {
            if ( this.oafrDataFormatChanged && this.autoStart)
            {
                this.oafrDataFormatChanged = false;
                this.StartFMODSound(AudioSettings.outputSampleRate);
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (data.Length != this.unityOAFRDataLength || channels != this.unityOAFRChannels)
            {
                this.unityOAFRDataLength = data.Length;
                this.unityOAFRChannels = channels;

                AudioStreamSupport.LOG(LogLevel.DEBUG, this.logLevel, this.gameObjectName, null, "OAFR START {0} {1} {2}", this.unityOAFRDataLength, this.unityOAFRChannels, AudioSettings.dspTime);

                this.oafrDataFormatChanged = true;
            }

            if (channel != null)
            {
                var arr = this.FloatArrayToByteArray(data);
                // lock for PCMReadCallback
                lock (this.pcmReadCallbackBufferLock)
                {
                    this.pcmReadCallbackBuffer[this.pcmReadCallback_ActiveBuffer].AddRange(arr);
                }

                // the data have just been passed to fmod for processing - clear unity buffer - this component should be the last one in audio chain, i.e. at the bottom in the inspector
                Array.Clear(data, 0, data.Length);
            }
        }

        void OnDisable()
        {
            this.StopFMODSound();

            if (this.pcmReadCallbackBuffer != null)
            {
                this.pcmReadCallbackBuffer[0].Clear();
                this.pcmReadCallbackBuffer[1].Clear();

                this.pcmReadCallbackBuffer[0] = null;
                this.pcmReadCallbackBuffer[1] = null;

                this.pcmReadCallbackBuffer.Clear();
                this.pcmReadCallbackBuffer = null;
            }

            this.pcmreadcallback = null;
            this.pcmsetposcallback = null;

            if (system != null)
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
        #region Start / Stop

        void StartFMODSound(int sampleRate)
        {
            this.StopFMODSound();

            FMOD.CREATESOUNDEXINFO exinfo = new FMOD.CREATESOUNDEXINFO();
            // exinfo.cbsize = sizeof(FMOD.CREATESOUNDEXINFO);
            exinfo.numchannels = this.unityOAFRChannels;                                                                              /* Number of channels in the sound. */
            exinfo.defaultfrequency = sampleRate;                                                                           /* Default playback rate of sound. */
            exinfo.decodebuffersize = (uint)this.unityOAFRDataLength;                                                                   /* Chunk size of stream update in samples. This will be the amount of data passed to the user callback. */
            exinfo.length = (uint)(exinfo.defaultfrequency * exinfo.numchannels * this.elementSize);                                        /* Length of PCM data in bytes of whole song (for Sound::getLength) */
            exinfo.format = FMOD.SOUND_FORMAT.PCM16;                                                                                        /* Data format of sound. */
            exinfo.pcmreadcallback = this.pcmreadcallback;                                                                                  /* User callback for reading. */
            exinfo.pcmsetposcallback = this.pcmsetposcallback;                                                                              /* User callback for seeking. */

            result = system.createSound(""
                , FMOD.MODE.OPENUSER
                | FMOD.MODE.CREATESTREAM
                | FMOD.MODE.LOOP_NORMAL
                , ref exinfo
                , out sound);
            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, null, "system.createSound");

            AudioStreamSupport.LOG(LogLevel.DEBUG, this.logLevel, this.gameObjectName, null, "About to play...");

            result = system.playSound(sound, null, false, out channel);
            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, null, "system.playSound");
        }

        void StopFMODSound()
        {
            if (channel != null)
            {
                result = channel.stop();
                // ERRCHECK(result, "channel.stop", false);
            }

            channel = null;

            System.Threading.Thread.Sleep(50);

            if (sound != null)
            {
                result = sound.release();
                // ERRCHECK(result, "sound.release", false);
            }

            sound = null;

            if (this.pcmReadCallbackBuffer != null)
            {
                this.pcmReadCallbackBuffer[0].Clear();
                this.pcmReadCallbackBuffer[1].Clear();
            }
        }

        #endregion

        // ========================================================================================================================================
        #region fmod buffer callbacks
        List<List<byte>> pcmReadCallbackBuffer = null;
        object pcmReadCallbackBufferLock = new object();
        int pcmReadCallback_ActiveBuffer = 0;

        [AOT.MonoPInvokeCallback(typeof(FMOD.SOUND_PCMREADCALLBACK))]
        FMOD.RESULT PCMReadCallback(System.IntPtr soundraw, System.IntPtr data, uint datalen)
        {
            // lock on pcmReadCallbackBuffer - can be changed ( added to ) in OAFR - here e.g. the underlying array can be changed while performing ToArray() leading to
            // 'Destination array was not long enough. Check destIndex and length, and the array's lower bounds'
            lock (this.pcmReadCallbackBufferLock)
            {
                var count = this.pcmReadCallbackBuffer[this.pcmReadCallback_ActiveBuffer].Count;

                AudioStreamSupport.LOG(LogLevel.DEBUG, this.logLevel, this.gameObjectName, null, "PCMReadCallback requested {0} while having {1}, time: {2}", datalen, count, AudioSettings.dspTime);

                if (count > 0 && count > datalen && datalen > 0)
                {
                    var bArr = this.pcmReadCallbackBuffer[this.pcmReadCallback_ActiveBuffer].ToArray();

                    System.Runtime.InteropServices.Marshal.Copy(bArr, 0, data, (int)datalen);

                    this.pcmReadCallbackBuffer[1 - this.pcmReadCallback_ActiveBuffer].AddRange(this.pcmReadCallbackBuffer[this.pcmReadCallback_ActiveBuffer].GetRange((int)datalen, count - (int)datalen));

                    this.pcmReadCallbackBuffer[this.pcmReadCallback_ActiveBuffer].Clear();

                    this.pcmReadCallback_ActiveBuffer = 1 - this.pcmReadCallback_ActiveBuffer;
                }
            }

            return FMOD.RESULT.OK;
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.SOUND_PCMSETPOSCALLBACK))]
        FMOD.RESULT PCMSetPosCallback(System.IntPtr soundraw, int subsound, uint position, FMOD.TIMEUNIT postype)
        {
            AudioStreamSupport.LOG(LogLevel.DEBUG, this.logLevel, this.gameObjectName, null, "PCMSetPosCallback requesting position {0} ", position);
            return FMOD.RESULT.OK;
        }
        #endregion

        // ========================================================================================================================================
        #region support

        byte[] FloatArrayToByteArray(float[] arr)
        {
            var result = new List<byte>();
            foreach (var f in arr)
                result.AddRange(this.FloatToByteArray(f));

            return result.ToArray();
        }

        byte[] FloatToByteArray(float f)
        {
            var result = new byte[2];

            var fa = (short)(f * 32768f);
            byte b0 = (byte)(fa >> 8);
            byte b1 = (byte)(fa & 0xFF);

            result[0] = b1;
            result[1] = b0;

            return result;
        }

        #endregion

        // ========================================================================================================================================
        #region user support
        public void SetOutput(int _outputDriverID)
        {
            AudioStreamSupport.LOG(LogLevel.INFO, this.logLevel, this.gameObjectName, null, "Setting output to driver {0} ", _outputDriverID);

            result = system.setDriver(_outputDriverID);
            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, null, "system.setDriver");

            this.outputDriverID = _outputDriverID;
        }

        public void StartRedirectWithSampleRate(int sampleRate)
        {
            this.StartFMODSound(sampleRate);
        }

        public void StopRedirect()
        {
            this.StopFMODSound();

        }
        #endregion
    }
}
