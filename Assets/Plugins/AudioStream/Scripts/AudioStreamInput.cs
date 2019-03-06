// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AudioStream
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioStreamInput : MonoBehaviour
    {
        // ========================================================================================================================================
        #region Editor

        // "No. of audio channels provided by selected recording device.
        [HideInInspector]
        public int recChannels = 0;

        [Header("[Source]")]

        [Tooltip("Audio input driver ID")]
        public int recordDeviceId = 0;

        [Header("[Setup]")]

        [Tooltip("Turn on/off logging to the Console. Errors are always printed.")]
        public LogLevel logLevel = LogLevel.ERROR;

        [Tooltip("When checked the recording will start on.. start. Otherwise use Record() method of this GameObject.")]
        public bool recordOnStart = true;

        #region Unity events
        [Header("[Events]")]
        public EventWithStringParameter OnRecordingStarted;
        public EventWithStringBoolParameter OnRecordingPaused;
        public EventWithStringParameter OnRecordingStopped;
        public EventWithStringStringParameter OnError;
        #endregion
        /// <summary>
        /// OAFR debug info
        /// </summary>
        string gameObjectName = string.Empty;
        #endregion

        // ========================================================================================================================================
        #region Init && FMOD structures
        /// <summary>
        /// Component startup sync
        /// Also in case of recording FMOD needs some time to enumerate all present recording devices - we need to wait for it. Check this flag when using from scripting.
        /// </summary>
        // TODO: query FMOD state properly instead of just waiting fixed amount of time
        [HideInInspector]
        public bool ready = false;

        FMOD.System system = null;
        FMOD.Sound sound = null;
        FMOD.RESULT result;
        FMOD.RESULT lastError = FMOD.RESULT.OK;
        FMOD.CREATESOUNDEXINFO exinfo;
        int key, count;
        uint version;
        uint datalength = 0, soundlength;
        uint lastrecordpos = 0;

        IEnumerator Start()
        {
            this.gameObjectName = this.gameObject.name;

            // setup the AudioSource
            var audiosrc = this.GetComponent<AudioSource>();
            audiosrc.playOnAwake = false;
            audiosrc.Stop();
            audiosrc.clip = null;

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

                if (this.OnError != null)
                    this.OnError.Invoke(this.gameObjectName, msg);

                throw new System.Exception(msg);
            }

            /*
            System initialization
            */
            result = system.init(100, FMOD.INITFLAGS.NORMAL, System.IntPtr.Zero);
            ERRCHECK(result, "system.init");

            // wait for FMDO to catch up and pause - recordDrivers are not populated if called immediately [e.g. from Start]
            yield return new WaitForSeconds(1f);

			if (this.recordOnStart)
				StartCoroutine (this.Record ());

            this.ready = true;
        }
        #endregion

        // ========================================================================================================================================
        #region Recording
        [Tooltip("Set during recording.")]
        public bool isRecording = false;
        [Tooltip("Set during recording.")]
        public bool isPaused = false;
        /// <summary>
        /// FMOD recording buffers and their lengths
        /// </summary>
        System.IntPtr ptr1, ptr2;
        uint len1, len2;

		public IEnumerator Record()
        {
            if (this.isRecording)
            {
                LOG(LogLevel.WARNING, "Already recording.");
				yield break;
            }

            if (!this.isActiveAndEnabled)
            {
                LOG(LogLevel.WARNING, "Will not start on disabled GameObject.");
				yield break;
            }

            this.isRecording = false;
            this.isPaused = false;

            this.Stop_Internal(); // try to clean partially started recording

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

				if (this.OnError != null)
					this.OnError.Invoke(this.gameObjectName, msg);

				throw new System.Exception(msg);
			}

			/*
            System initialization
            */
			result = system.init(100, FMOD.INITFLAGS.NORMAL, System.IntPtr.Zero);
			ERRCHECK(result, "system.init");

			// wait for FMDO to catch up and pause - recordDrivers are not populated if called immediately [e.g. from Start]
			yield return new WaitForSeconds(1f);

            LOG(LogLevel.INFO, "Setting audio output on default (earspeaker)...");
            iOSSpeaker.RouteNormal();

            StartCoroutine(this.RecordCR());
        }

        IEnumerator RecordCR()
        {
            int recRate = 0;
            int namelen = 255;
            System.Text.StringBuilder name = new System.Text.StringBuilder(namelen);
            System.Guid guid;
            FMOD.SPEAKERMODE speakermode;
            FMOD.DRIVER_STATE driverstate;
            result = system.getRecordDriverInfo(this.recordDeviceId, name, namelen, out guid, out recRate, out speakermode, out recChannels, out driverstate);
            ERRCHECK(result, "system.getRecordDriverInfo");

            // compensate the input rate for the current output rate
            this.GetComponent<AudioSource>().pitch = ((float)(recRate * recChannels) / (float)(AudioSettings.outputSampleRate * (int)AudioSettings.speakerMode));

            exinfo = new FMOD.CREATESOUNDEXINFO();
            exinfo.numchannels = recChannels;
            exinfo.format = FMOD.SOUND_FORMAT.PCM16;
            exinfo.defaultfrequency = recRate;
            exinfo.length = (uint)(recRate * sizeof(short) * recChannels);

            result = system.createSound(string.Empty, FMOD.MODE.LOOP_NORMAL | FMOD.MODE.OPENUSER, ref exinfo, out sound);
            ERRCHECK(result, "system.createSound");

            this.GetComponent<AudioSource>().Play();

            result = system.recordStart(this.recordDeviceId, sound, true);
            ERRCHECK(result, "system.recordStart");

            result = sound.getLength(out soundlength, FMOD.TIMEUNIT.PCM);
            ERRCHECK(result, "sound.getLength");

            this.isRecording = true;

            if (this.OnRecordingStarted != null)
                this.OnRecordingStarted.Invoke(this.gameObjectName);

            for (;;)
            {
                result = system.update();
                ERRCHECK(result, "system.update", false);

                if (this.isPaused)
                    yield return null;

                uint recordpos = 0;

                system.getRecordPosition(this.recordDeviceId, out recordpos);
                ERRCHECK(result, "system.getRecordPosition");

                if (recordpos != lastrecordpos)
                {
                    int blocklength;

                    blocklength = (int)recordpos - (int)lastrecordpos;
                    if (blocklength < 0)
                    {
                        blocklength += (int)soundlength;
                    }

                    /*
                    Lock the sound to get access to the raw data.
                    */
                    result = sound.@lock((uint)(lastrecordpos * exinfo.numchannels * 2), (uint)(blocklength * exinfo.numchannels * 2), out ptr1, out ptr2, out len1, out len2);   /* * exinfo.numchannels * 2 = stereo 16bit.  1 sample = 4 bytes. */

                    /*
                    Write it to output.
                    */
                    if (ptr1.ToInt64() != 0 && len1 > 0)
                    {
                        datalength += len1;
                        byte[] barr = new byte[len1];
                        Marshal.Copy(ptr1, barr, 0, (int)len1);

                        this.AddBytesToOutputBuffer(barr);
                    }
                    if (ptr2.ToInt64() != 0 && len2 > 0)
                    {
                        datalength += len2;
                        byte[] barr = new byte[len2];
                        Marshal.Copy(ptr2, barr, 0, (int)len2);
                        this.AddBytesToOutputBuffer(barr);
                    }

                    /*
                    Unlock the sound to allow FMOD to use it again.
                    */
                    result = sound.unlock(ptr1, ptr2, len1, len2);
                }

                lastrecordpos = recordpos;

                // print(string.Format("Record buffer pos = {0} : Record time = {1}:{2}", recordpos, datalength / exinfo.defaultfrequency / exinfo.numchannels / 2 / 60, (datalength / exinfo.defaultfrequency / exinfo.numchannels / 2) % 60));

                // System.Threading.Thread.Sleep(10);

                yield return null;
            }
        }

        public void Pause(bool pause)
        {
            if (!this.isRecording)
            {
                LOG(LogLevel.WARNING, "Not recording..");
                return;
            }

            this.isPaused = pause;

            LOG(LogLevel.INFO, "{0}", this.isPaused ? "paused." : "resumed.");

            if (this.OnRecordingPaused != null)
                this.OnRecordingPaused.Invoke(this.gameObjectName, this.isPaused);
        }

        #endregion

        // ========================================================================================================================================
        #region Shutdown
        public void Stop()
        {
            if (!this.isRecording)
                return;

            LOG(LogLevel.INFO, "Stopping..");

            this.StopAllCoroutines();

			/*
             * clear FMOD buffer/s - they like to be reused, and reset rec position -
             */
			this.lastrecordpos = 0;

			if (ptr1.ToInt64() != 0 && ptr1 != System.IntPtr.Zero && len1 > 0)
			{
				byte[] barr = new byte[len1];
				for (int i = 0; i < barr.Length; ++i) barr[i] = 0;
				Marshal.Copy(barr, 0, ptr1, (int)len1);
			}

			if (ptr2.ToInt64() != 0 && ptr2 != System.IntPtr.Zero && len2 > 0)
			{
				byte[] barr = new byte[len2];
				for (int i = 0; i < barr.Length; ++i) barr[i] = 0;
				Marshal.Copy(barr, 0, ptr2, (int)len2);
			}

			this.Stop_Internal();

            if (this.OnRecordingStopped != null)
                this.OnRecordingStopped.Invoke(this.gameObjectName);
        }

        /// <summary>
        /// Stop and try to release FMOD sound resources
        /// </summary>
        void Stop_Internal()
        {
            this.GetComponent<AudioSource>().Stop();

            this.isRecording = false;
            this.isPaused = false;

            /*
                Shut down sound
            */
            if (sound != null)
            {
                result = sound.release();
                ERRCHECK(result, "sound.release", false);
            }

            sound = null;

			/*
                Shut down
            */
			if (system != null)
			{
				result = system.close();
				ERRCHECK(result, "system.close", false);

				result = system.release();
				ERRCHECK(result, "system.release", false);
			}

			system = null;
        }

        void OnDisable()
        {
            this.Stop();
        }
        #endregion


        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!this.isRecording || this.isPaused)
                return;

            if (sound != null)
            {
                var fArr = this.GetAudioOutputBuffer((uint)data.Length);
                if (fArr != null)
                {
                    int length = fArr.Length;
                    for (int i = 0; i < length; ++i)
                        data[i] = fArr[i];
                }
            }
        }

        // ========================================================================================================================================
        #region support
        List<byte> outputBuffer = new List<byte>();
        object outputBufferLock = new object();

        void AddBytesToOutputBuffer(byte[] arr)
        {
            lock (this.outputBufferLock)
            {
                outputBuffer.AddRange(arr);
            }
        }

        float[] oafrDataArr = null; // instance buffer
        float[] GetAudioOutputBuffer(uint _len)
        {
            lock (this.outputBufferLock)
            {
                // 2 bytes per 1 value - adjust requested length
                uint len = _len * 2;

                if (len > outputBuffer.Count)
                    return null;

                byte[] bArr = outputBuffer.GetRange(0, (int)len).ToArray();
                outputBuffer.RemoveRange(0, (int)len);

                AudioStreamSupport.ByteArrayToFloatArray(bArr, (uint)bArr.Length, ref oafrDataArr);

                return this.oafrDataArr;
            }
        }

        void ERRCHECK(FMOD.RESULT result, string customMessage, bool throwOnError = true)
        {
            this.lastError = result;

            AudioStreamSupport.ERRCHECK(result, this.logLevel, this.gameObjectName, this.OnError, customMessage, throwOnError);
        }

        void LOG(LogLevel requestedLogLevel, string format, params object[] args)
        {
            AudioStreamSupport.LOG(requestedLogLevel, this.logLevel, this.gameObjectName, this.OnError, format, args);
        }

        public string GetLastError(out FMOD.RESULT errorCode)
        {
            errorCode = this.lastError;
            return FMOD.Error.String(errorCode);
        }
        #endregion

        // ========================================================================================================================================
        #region User support
        /// <summary>
        /// Enumerates available audio inputs in the system and returns their names.
        /// </summary>
        /// <returns></returns>
        public List<string> AvailableInputs()
        {
            List<string> availableDriversNames = new List<string>();

            /*
            Enumerate record devices
            */
            int numAllDrivers = 0;
            int numConnectedDrivers = 0;
            result = system.getRecordNumDrivers(out numAllDrivers, out numConnectedDrivers);
            ERRCHECK(result, "system.getRecordNumDrivers");

            for (int i = 0; i < numConnectedDrivers; ++i)
            {
                int recChannels = 0;
                int recRate = 0;
                int namelen = 255;
                System.Text.StringBuilder name = new System.Text.StringBuilder(namelen);
                System.Guid guid;
                FMOD.SPEAKERMODE speakermode;
                FMOD.DRIVER_STATE driverstate;
                result = system.getRecordDriverInfo(i, name, namelen, out guid, out recRate, out speakermode, out recChannels, out driverstate);
                ERRCHECK(result, "system.getRecordDriverInfo");

                var description = string.Format("{0} rate: {1} speaker mode: {2} channels: {3}", name, recRate, speakermode, recChannels);

                availableDriversNames.Add(description);

                LOG(LogLevel.DEBUG, "{0}{1}guid: {2}{3}systemrate: {4}{5}speaker mode: {6}{7}channels: {8}{9}state: {10}"
                    , name
                    , System.Environment.NewLine
                    , guid
                    , System.Environment.NewLine
                    , recRate
                    , System.Environment.NewLine
                    , speakermode
                    , System.Environment.NewLine
                    , recChannels
                    , System.Environment.NewLine
                    , driverstate
                    );
            }

            return availableDriversNames;
        }
        #endregion
    }
}
