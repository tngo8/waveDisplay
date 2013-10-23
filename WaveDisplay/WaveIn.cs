﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO;
using NAudio.Dsp;


namespace WaveDisplay
{
    public class WaveIn
    {
        public struct notePredict
        {
            public string NoteName;
            public short octave;
            public int percentage;
            public float duration;
        }

        public struct noteInterval
        {
            public int startIdx;
            public int endIdx;
        }

        public List<notePredict> notePredictList=new List<notePredict>();
        public struct waveHeader
        {
            public byte[] riffID;
            public byte[] fileFormat;
            public byte[] fmtID;
            public UInt16 audioFormat;
            public UInt16 nChannels;
            public uint sampleRate;
            public uint byteRate;
            public UInt16 blockAlign;
            public UInt16 bitSample;
            public byte[] dataID;
            public uint dataSize;
        }
        public waveHeader wavHeader;

        public List<short> leftData = new List<short>(); //Aussme if filewave is mono, use this leftData to extract data
        public List<short> rightData = new List<short>();
        public List<List<float>> stftWav = new List<List<float>>(); //contain all fft chunks to form STFT data
        public List<double> spectroPow = new List<double>(); //List with each element is the total power of each STFT chunk 

        public WaveIn(WaveIn previousWave)
        {
            leftData = previousWave.leftData;
            rightData = previousWave.rightData;
        }
        public WaveIn()
        {
            leftData.Clear();
            rightData.Clear();
            stftWav.Clear();
        }

        public void waveExtract(string filename)
        {
            byte[] tmpByte;
            try
            {
                using (FileStream wave_fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    var test = wave_fs.Length;
                    using (BinaryReader br = new BinaryReader(wave_fs))
                    {
                        wavHeader.riffID = br.ReadBytes(4);
                        tmpByte = br.ReadBytes(4);
                        wavHeader.fileFormat = br.ReadBytes(4);
                        wavHeader.fmtID = br.ReadBytes(4);
                        tmpByte = br.ReadBytes(4);
                        wavHeader.audioFormat = br.ReadUInt16();
                        wavHeader.nChannels = br.ReadUInt16();
                        wavHeader.sampleRate = br.ReadUInt32();
                        wavHeader.byteRate = br.ReadUInt32();
                        wavHeader.blockAlign = br.ReadUInt16();
                        wavHeader.bitSample = br.ReadUInt16();
                        wavHeader.dataID = br.ReadBytes(4);
                        wavHeader.dataSize = br.ReadUInt32();
                        //assume the file read in has 16 bit per sample
                        for (int i = 0; i < (wave_fs.Length - 44) / wavHeader.blockAlign; i++)
                        {
                            if (wavHeader.nChannels == 1)
                            {
                                if (wavHeader.bitSample == 8)
                                    leftData.Add((short)((br.ReadByte()-128)*65536/256));
                                else
                                    leftData.Add(br.ReadInt16());
                            }
                            else if (wavHeader.nChannels == 2)
                            {
                                if (wavHeader.bitSample == 8)
                                {
                                    leftData.Add((short)(br.ReadByte()));
                                    rightData.Add((short)(br.ReadByte()));
                                }
                                else
                                {
                                    leftData.Add(br.ReadInt16());
                                    rightData.Add(br.ReadInt16());
                                }
                            }
                        }

                    }
                    string riff = Encoding.UTF8.GetString(wavHeader.riffID);
                    string fmt = Encoding.UTF8.GetString(wavHeader.fmtID);
                    string format = Encoding.UTF8.GetString(wavHeader.fileFormat);
                    Console.WriteLine("wav File header " + riff + " " + format + " " + wavHeader.audioFormat.ToString());
                }
            }

            catch (FileNotFoundException e)
            {
                MessageBox.Show("cannot open the file " + e.FileName);
            }

        }

        public Bitmap DrawAudio(List<short> inputValues, PictureBox picDraw)
        {
            Bitmap bmp;
            int index = 0;
            int width = picDraw.Width;
            int height = picDraw.Height;
            SizeF sizef = new SizeF();
            RectangleF rectanglef = new RectangleF();
            bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                if (inputValues.Count >= 20000)
                {
                    List<short> inputNomalized = new List<short>();
                    inputNomalized = waveNormalize(inputValues, width, height);
                    for (int i = 0; i < (inputNomalized.Count) / 2 - 1; i++)
                    {
                        g.DrawLine(new Pen(Color.Blue), new Point(i, inputNomalized[index] + height / 2), new Point(i, inputNomalized[index + 1] + height / 2));
                        index += 2;
                    }

                }
                else
                {
                    PointF previous = new PointF(0, height / 2);
                    for (int i = 0; i < inputValues.Count; i++)
                    {
                        float x = (float)(i * 1.0 * width / inputValues.Count);
                        float y = (float)height / 2 + inputValues[i] * height / (1 << 16);
                        PointF next = new PointF(x, y);
                        g.DrawLine(new Pen(Color.Green), previous, next);
                        if (inputValues.Count <= 500)
                        {
                            sizef = new SizeF(2, 2);
                            rectanglef = new RectangleF(previous, sizef);
                            g.FillRectangle(new SolidBrush(Color.Gray), rectanglef);
                        }
                        previous = next;
                    }
                }

            }
            picDraw.Image = bmp;
            return bmp;
        }

        public List<Int16> waveNormalize(List<short> inputValues, int pxRangeX, int pxRangeY) //normalized data to fit in window
        {
            int dataPerPixel = inputValues.Count / pxRangeX;
            List<short> tempValues = new List<short>();
            List<short> outputValues = new List<short>();
            int index = 0;
            for (int i = 0; i + dataPerPixel <= inputValues.Count; i += dataPerPixel)
            {
                tempValues = inputValues.GetRange(i, dataPerPixel);
                outputValues.Add(tempValues.Max());
                outputValues.Add(tempValues.Min());
                outputValues[index] = (short)((outputValues[index] * pxRangeY) / 65536);
                outputValues[index + 1] = (short)((outputValues[index + 1] * pxRangeY) / 65536);
                index += 2;
            }
            return outputValues;
        }      

        public void spectroDiffDraw(Chart outputChart)
        {
            outputChart.Series.Clear();
            outputChart.Series.Add("corrSeries");
            outputChart.Series["corrSeries"].ChartType = SeriesChartType.FastLine;
            foreach (double item in spectroPow)
            {
                outputChart.Series["corrSeries"].Points.AddY((double)item);
            }
        }

        public Bitmap spectrogram( PictureBox picdraw,int frange)
        {
            int NoFFt = stftWav[0].Count;
            int i, j;
            Bitmap bmp = new Bitmap(picdraw.Width, picdraw.Height);
            List<float> maxRange = new List<float>();
            float maxData = stftWav.Max(column => column.GetRange(0, frange).Max());
            maxData=(float)(Math.Log10(maxData));
            RectangleF rectF = new RectangleF();
            SizeF rectFsize = new SizeF();
            PointF coordF = new PointF();
            float Xscale = (float)picdraw.Width / stftWav.Count;
            float Yscale = (float)picdraw.Height / frange;
            rectFsize = new SizeF(Xscale, Yscale);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                for (i = 0; i < stftWav.Count; i++)
                {
                    for (j = 0; j < frange; j++)  //higher frequencies are plotted from the bottom of screen
                    {
                        coordF = new PointF(i * Xscale, j * Yscale);
                        rectF = new RectangleF(coordF, rectFsize);
                        float colorData = stftWav[i][(NoFFt / 2 - 1) - j];
                        colorData = (colorData > 1) ? (float)Math.Log10(colorData) : 0;
                        g.FillRectangle(new SolidBrush(getColor(colorData, maxData)), rectF);
                    }
                }
            }
            return bmp;
        }

        public Color getColor(float input, float maxData)
        {

            float colorFactor = input / maxData;
            Color setColor1;
            //Console.WriteLine("colorFactor: " + colorFactor.ToString());
            int newIntensity = 0;
            newIntensity = (int)(255 * colorFactor);
            if (newIntensity > 50)
            {
                setColor1 = Color.FromArgb(255, newIntensity, newIntensity, newIntensity);
                //setColor2 = ControlPaint.Light(setColor1, 0.8f);
            }

            else
            {
                setColor1 = Color.FromArgb(255, newIntensity , newIntensity , 0);
                //setColor2 = ControlPaint.Light(setColor1, 0.6f);
            }

            return setColor1;

        }
        

        public void STFT(List<short> inputValues, short No)
        {
            int i, overLap = No / 2; //overLap 50%
            List<float> fftChunk = new List<float>();
            List<float> fftChunktmp;
            int count = inputValues.Count;
            for (i = 0; i + No < count; i += overLap)
            {
                fftChunktmp = FFT(inputValues.GetRange(i, No));
                fftChunk = fftChunktmp.GetRange(0, fftChunktmp.Count / 2);

                stftWav.Add(fftChunk);
            }
            fftChunktmp = FFT(inputValues.GetRange(count - No, No)); ;
            fftChunk = fftChunktmp.GetRange(0, fftChunktmp.Count / 2);
            stftWav.Add(fftChunk);

            for (int n = 0; n < stftWav.Count; n++)
            {
               
                //int fstart = (int)(3000 / (wavHeader.sampleRate / No));
                //int fend = (int)(5000 / (wavHeader.sampleRate / No));
                //List<double> tempList = new List<double>();
                //for (int k = fstart; k < fend; k++) //onset detection function focus on high frequency content
                //   // 1st option: calculate the spectral difference for onset detection 

                //    tempList.Add(stftWav[n][k]-stftWav[n-1][k]);
                    
                //    //2nd options: use high frequency content method
                //   tempList.Add(Math.Pow(k,2)*Math.Pow(stftWav[n][k],2));
                //}
                //double sum = stftWav[n].Sum(X=>Math.Pow(X+Math.Abs(X)/2,2));
                double sum = stftWav[n].Sum(X=>Math.Pow(X,2));
                spectroPow.Add(sum);                            
            }
            //Apply moving average filter to smooth the curve   
            double[] pad = new double[3];
            spectroPow.InsertRange(0, pad);
            spectroPow.AddRange(pad);
            for (int m = 3; m < spectroPow.Count - 3; m++)
            {
                var newVal = spectroPow.GetRange(m - 3, 6).Average();
                spectroPow[m] = newVal;
            }
            spectroPow.RemoveRange(0, 3);
            spectroPow.RemoveRange(spectroPow.Count - 3, 3);

        }
        public List<float> FFT(List<short> inputValues)
        {
            int N = inputValues.Count; //assume N =2^n
            int n = (int)(Math.Log(N) / Math.Log(2));
            int i;
            List<float> Real = inputValues.ConvertAll(y => (float)y);
            List<float> Imagine = new List<float>();
            for (i = 0; i < inputValues.Count; i++)
            {
                Imagine.Add(0.0f);
            }
            //Naudio FFT test
            Complex[] Data = new Complex[Real.Count];
            for (int l = 0; l < Real.Count; l++)
            {
                Data[l].X = Real[l];
                Data[l].Y = Imagine[l];
            }

            NAudio.Dsp.FastFourierTransform.FFT(true, n, Data);

            List<float> output = new List<float>();
            float outputMag;
            for (i = 0; i < N ; i++)
            {
                outputMag = (float)Math.Sqrt(Math.Pow(Data[i].X, 2) + Math.Pow(Data[i].Y, 2));
                output.Add(outputMag);
            }
            return output;
        }

        public List<noteInterval> onSetDetect(int No)
        {
            List<noteInterval> noteMarkedList = new List<noteInterval>(); //each int[] is a pair of the waveform indices of a note
            //define rest threshold
            List<double> sortedSpectroPow = spectroPow.OrderBy(data => data).ToList();
            var rest = sortedSpectroPow[(int)(0.08 * sortedSpectroPow.Count)];
            int i=0;
            //get rid of first rest time in some file/ save time of processing
            while (spectroPow[i] <= rest)
                i++;
            noteInterval note ; //content the stft chunk index start and end of the note onset detection
            note.startIdx = -1;
            note.endIdx = -1;

            
            string PrePitchName = "N";
            while (i + 14 < spectroPow.Count)
            {   
                 //time index of the left range at the start point of the first note
                int TimeStart = (int)(i * No / 2);
                int TimeEnd = (int)((i + 14) * No / 2 + No);
                List<float> PitchCorr = autocorrelation(leftData.GetRange(TimeStart, TimeEnd-TimeStart), wavHeader.sampleRate);
                notePredict Pitch = noteIdentify(PitchCorr, wavHeader.sampleRate, TimeEnd-TimeStart); //using 5460 to reduce padding
                                                                                    //iteration in autocorrelate function
                if (Pitch.NoteName == "Err")
                {                   
                    var Chunk = spectroPow.GetRange(i, 9);
                    //check for valley
                    int MinIdx = Chunk.IndexOf(Chunk.Min());
                    if (MinIdx - 2 >= 0 && MinIdx + 2 < Chunk.Count)
                    {
                        if (Chunk[MinIdx - 1] >= Chunk[MinIdx] && Chunk[MinIdx + 1] >= Chunk[MinIdx])
                        {
                            if (Chunk[MinIdx - 2] >= Chunk[MinIdx - 1] && Chunk[MinIdx + 2] >= Chunk[MinIdx + 1])
                            {
                                if (note.startIdx == -1)
                                    note.startIdx = i-1;
                                else
                                    note.endIdx = i + 8;
                                    
                            }
                        }
                    }
                }
                else if (Pitch.NoteName != "NaN")
                {

                    if (Pitch.NoteName[0].ToString() != PrePitchName[0].ToString())
                    {
                        if (note.startIdx == -1)
                            note.startIdx = i-1;
                        else
                            note.endIdx = i+ 8;
                        PrePitchName = Pitch.NoteName[0].ToString();
                        if (note.startIdx != -1 && note.endIdx != -1)
                        {
                            noteMarkedList.Add(note);
                            //check for long note that contain actually some notes of same pitch
                            if (note.endIdx - note.startIdx > 60)
                            {
                                //sorted the note chunk to find median
                                var spectroPowChunk = sortedSpectroPow.GetRange(note.startIdx, note.endIdx - note.startIdx);
                                var sortedChunk = spectroPowChunk.OrderBy(data => data).ToList();
                                var lowerThreshold = sortedChunk[(int)(0.2 * sortedChunk.Count)];
                                //moving window to find a valley
                                List<int> extraMark = new List<int>();
                                for (int k = 0; k + 20 < spectroPowChunk.Count; k += 20)
                                {
                                    var window = spectroPowChunk.GetRange(k, 20);
                                    if(window.Min()<lowerThreshold)
                                    {
                                        extraMark.Add(window.IndexOf(window.Min())+k);
                                    }
                                }
                                if (extraMark.Count > 0)
                                {
                                    var newStart = noteMarkedList.Last().startIdx;
                                    var newEnd = noteMarkedList.Last().endIdx;
                                    noteMarkedList.RemoveAt(noteMarkedList.Count - 1);
                                    noteInterval newNote;
                                    for (int e = 0; e < extraMark.Count; e++)
                                    {
                                        newNote.startIdx = newStart;
                                        if (e != extraMark.Count - 1)
                                            newNote.endIdx = extraMark[e];
                                        else
                                            newNote.endIdx = newEnd;
                                        noteMarkedList.Add(newNote);
                                        newStart = extraMark[e];
                                    }
                                }
                            }
                            note.startIdx = i+8;
                            note.endIdx = -1;
                        }
                    }
                    
                }
                if (note.startIdx != -1 && note.endIdx != -1)
                    {
                        note.startIdx = -1;
                        note.endIdx = -1;
                    }
                
                i+=5;
            }          
            //remove all the note that have a chunk duration of only 17 and under
            noteMarkedList.RemoveAll(item => item.endIdx - item.startIdx <= 17);
            return noteMarkedList;
        }

        public List<float> autocorrelation(List<short> dataInput, uint sampleRate)
        { 
            //extend the length of dataInput to further 1/2 of its length by zeros padding, dataInput is time inteval values
            List<float> FFToutput = FFT(dataInput);
            int N = FFToutput.Count; //assume N =2^n
            float fres = sampleRate /(float) N;
            int[] cutoffIdx = new int[2];

            //bandpass filter, only keep frequency between 195 to 4400Hz
            cutoffIdx[0] = (int)(195 / fres);
            cutoffIdx[1] = (int)(4400 / fres);
            for (int a = 0; a < cutoffIdx[0]; a++)
            {
                FFToutput[a] = 0;
            }
            for (int b = cutoffIdx[1]; b < FFToutput.Count; b++)
            {
                FFToutput[b] = 0;
            }
            int n = (int)(Math.Log(N) / Math.Log(2));
            int i;
            List<float> IReal = new List<float>(FFToutput);
            List<float> IImagine = new List<float>();
            for (i = 0; i < dataInput.Count; i++)
            {
                IImagine.Add(0.0f);
            }
            //Naudio FFT test
            Complex[] IData = new Complex[IReal.Count];
            for (int l = 0; l < IReal.Count; l++)
            {
                IData[l].X = IReal[l];
                IData[l].Y = IImagine[l];
            }

            NAudio.Dsp.FastFourierTransform.FFT(false, n, IData);

            List<float> Ioutput = new List<float>();
            float IoutputMag;
            for (i = 0; i < N; i++)
            {
                IoutputMag = (float)Math.Sqrt(Math.Pow(IData[i].X, 2) + Math.Pow(IData[i].Y, 2));
                Ioutput.Add(IoutputMag);
            }
            return Ioutput;
        }

        public notePredict noteIdentify(List<float> corrInput,uint sampleRate, int duration)
        {
            string[] noteSequence = new string[12] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            notePredict note = new notePredict(); 
            float corrMax = corrInput.Max();
            //Get data from the range 196kHZ to 4400Khz range of violin data
            List<float> localData = corrInput.GetRange((int)(sampleRate / 4400), (int)(sampleRate / 196-sampleRate/4400));

            //sort data from smallest to largest, then find median 
            List<float> sortedData = localData.OrderBy(data => data).ToList();
            float threshold = sortedData[(int)(0.95 * sortedData.Count)]; //peak usually lies at 95% of data
            //if threshold is NaN, this function return an unidentified note and is named as NaN"
            if (Single.IsNaN(threshold))
            { 
                note.NoteName = "NaN"; //indicate a rest 
                return note; 
            }

            int i = 0;

            while (corrInput[i] > threshold)  //remove all of high correlation value at the beginning 
            {
                if (i >= ((3.0f / 2) * sampleRate / 196))
                {
                    note.NoteName = "Err"; //unable to identify note
                    return note; 
                }
                i++;
            }
            while (corrInput[i] <= threshold) //pass threshold value 
            {
                if (i >= ((3.0f / 2) * sampleRate / 196))
                {
                    note.NoteName = "Err"; //unable to identify note
                    return note;
                }
                i++;
            }
            List<float> peakData = new List<float>();
            int j = i;
            while (corrInput[i] > threshold)
            {
                peakData.Add(corrInput[i]);
                i++;
            }
            int peakIndex=peakData.IndexOf(peakData.Max())+j;
            float fpeak = (float)sampleRate / peakIndex;
            double nlogfreq = Math.Log(fpeak/440,2)*12+9; //value in term on n/12 + 9 that makes C4 has n=0;
            int noteIndex = (int) Math.Round(nlogfreq); //return the integer term of n 
         
            
            note.percentage = (int)(100*(nlogfreq-noteIndex));
            if (noteIndex >= 0 && noteIndex < 12)
            {
                note.NoteName = noteSequence[noteIndex];
                note.octave=4;

            }
            else if (noteIndex < 0 && noteIndex >= -12)
            {
                note.NoteName = noteSequence[noteIndex+12];
                note.octave = 3;
            }
            else if (noteIndex >= 12 && noteIndex < 24)
            {
                note.NoteName = noteSequence[noteIndex -12];
                note.octave = 5;
            }

            else if (noteIndex >= 24 && noteIndex < 36)
            {
                note.NoteName = noteSequence[noteIndex -24 ];
                note.octave = 6;
            }

            else if (noteIndex >= 36 && noteIndex < 48)
            {
                note.NoteName = noteSequence[noteIndex -36];
                note.octave = 7;
            }
            else if (noteIndex >= 48 && noteIndex < 60)
            {
                note.NoteName = noteSequence[noteIndex - 48];
                note.octave = 8;
            }

            note.duration = (float)(duration)/sampleRate;
            return note;

        }
    }
}
