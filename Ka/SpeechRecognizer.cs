using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ka.Speech
{
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using Microsoft.Kinect;
        using Microsoft.Speech.AudioFormat;
        using Microsoft.Speech.Recognition;
        using Ka.Utils;

        public class SpeechRecognizer : IDisposable
        {
            private readonly Dictionary<string, WhatSaid> gameplayPhrases = new Dictionary<string, WhatSaid>
                {
                    { "Faster", new WhatSaid { Verb = Verbs.Faster } },
                    { "Slower", new WhatSaid { Verb = Verbs.Slower } },
                    { "More Shapes", new WhatSaid { Verb = Verbs.More } },
                    { "More", new WhatSaid { Verb = Verbs.More } },
                    { "Less", new WhatSaid { Verb = Verbs.Fewer } },
                    { "Fewer", new WhatSaid { Verb = Verbs.Fewer } },
                };


            private readonly Dictionary<string, WhatSaid> singlePhrases = new Dictionary<string, WhatSaid>
                {
                    { "Speed Up", new WhatSaid { Verb = Verbs.Faster } },
                    { "Slow Down", new WhatSaid { Verb = Verbs.Slower } },
                    { "Reset", new WhatSaid { Verb = Verbs.Reset } },
                    { "Clear", new WhatSaid { Verb = Verbs.Reset } },
                    { "Stop", new WhatSaid { Verb = Verbs.Pause } },
                    { "Pause Game", new WhatSaid { Verb = Verbs.Pause } },
                    { "Freeze", new WhatSaid { Verb = Verbs.Pause } },
                    { "Unfreeze", new WhatSaid { Verb = Verbs.Resume } },
                    { "Resume", new WhatSaid { Verb = Verbs.Resume } },
                    { "Continue", new WhatSaid { Verb = Verbs.Resume } },
                    { "Play", new WhatSaid { Verb = Verbs.Resume } },
                    { "Start", new WhatSaid { Verb = Verbs.Resume } },
                    { "Go", new WhatSaid { Verb = Verbs.Resume } },
                    { "Ka", new WhatSaid { Verb = Verbs.Ka } },
                };

            private SpeechRecognitionEngine sre;
            private KinectAudioSource kinectAudioSource;
            private bool paused;
            private bool isDisposed;

            private SpeechRecognizer()
            {
                RecognizerInfo ri = GetKinectRecognizer();
                this.sre = new SpeechRecognitionEngine(ri);
                this.LoadGrammar(this.sre);
            }

            public event EventHandler<SaidSomethingEventArgs> SaidSomething;

            public enum Verbs
            {
                None = 0,
                Ka,
                Bigger,
                Biggest,
                Smaller,
                Smallest,
                More,
                Fewer,
                Faster,
                Slower,
                Reset,
                Pause,
                Resume
            }

            public EchoCancellationMode EchoCancellationMode
            {
                get
                {
                    this.CheckDisposed();

                    return this.kinectAudioSource.EchoCancellationMode;
                }

                set
                {
                    this.CheckDisposed();

                    this.kinectAudioSource.EchoCancellationMode = value;
                }
            }

            // This method exists so that it can be easily called and return safely if the speech prereqs aren't installed.
            // We isolate the try/catch inside this class, and don't impose the need on the caller.
            public static SpeechRecognizer Create()
            {
                SpeechRecognizer recognizer = null;

                try
                {
                    recognizer = new SpeechRecognizer();
                }
                catch (Exception)
                {
                    // speech prereq isn't installed. a null recognizer will be handled properly by the app.
                }

                return recognizer;
            }

            public void Start(KinectAudioSource kinectSource)
            {
                this.CheckDisposed();

                this.kinectAudioSource = kinectSource;
                this.kinectAudioSource.AutomaticGainControlEnabled = false;
                this.kinectAudioSource.BeamAngleMode = BeamAngleMode.Adaptive;
                var kinectStream = this.kinectAudioSource.Start();
                this.sre.SetInputToAudioStream(
                    kinectStream, new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
                this.sre.RecognizeAsync(RecognizeMode.Multiple);
            }

            public void Stop()
            {
                this.CheckDisposed();

                if (this.sre != null)
                {
                    this.kinectAudioSource.Stop();
                    this.sre.RecognizeAsyncCancel();
                    this.sre.RecognizeAsyncStop();

                    this.sre.SpeechRecognized -= this.SreSpeechRecognized;
                    this.sre.SpeechHypothesized -= this.SreSpeechHypothesized;
                    this.sre.SpeechRecognitionRejected -= this.SreSpeechRecognitionRejected;
                }
            }

            public void Dispose()
            {
                this.Dispose(true);
                GC.SuppressFinalize(this);
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "sre",
                Justification = "This is suppressed because FXCop does not see our threaded dispose.")]
            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.Stop();

                    if (this.sre != null)
                    {
                        // NOTE: The SpeechRecognitionEngine can take a long time to dispose
                        // so we will dispose it on a background thread
                        ThreadPool.QueueUserWorkItem(
                            delegate(object state)
                                {
                                    IDisposable toDispose = state as IDisposable;
                                    if (toDispose != null)
                                    {
                                        toDispose.Dispose();
                                    }
                                },
                                this.sre);
                        this.sre = null;
                    }

                    this.isDisposed = true;
                }
            }

            private static RecognizerInfo GetKinectRecognizer()
            {
                Func<RecognizerInfo, bool> matchingFunc = r =>
                {
                    string value;
                    r.AdditionalInfo.TryGetValue("Kinect", out value);
                    return "True".Equals(value, StringComparison.InvariantCultureIgnoreCase) && "en-US".Equals(r.Culture.Name, StringComparison.InvariantCultureIgnoreCase);
                };
                return SpeechRecognitionEngine.InstalledRecognizers().Where(matchingFunc).FirstOrDefault();
            }

            private void CheckDisposed()
            {
                if (this.isDisposed)
                {
                    throw new ObjectDisposedException("SpeechRecognizer");
                }
            }

            private void LoadGrammar(SpeechRecognitionEngine speechRecognitionEngine)
            {
                // Build a simple grammar of shapes, colors, and some simple program control
                var single = new Choices();
                foreach (var phrase in this.singlePhrases)
                {
                    single.Add(phrase.Key);
                }

                var gameplay = new Choices();
                foreach (var phrase in this.gameplayPhrases)
                {
                    gameplay.Add(phrase.Key);
                }

                var objectChoices = new Choices();
                objectChoices.Add(gameplay);

                var actionGrammar = new GrammarBuilder();
                actionGrammar.AppendWildcard();
                actionGrammar.Append(objectChoices);

                var allChoices = new Choices();
                allChoices.Add(actionGrammar);
                allChoices.Add(single);

                // This is needed to ensure that it will work on machines with any culture, not just en-us.
                var gb = new GrammarBuilder { Culture = speechRecognitionEngine.RecognizerInfo.Culture };
                gb.Append(allChoices);

                var g = new Grammar(gb);
                speechRecognitionEngine.LoadGrammar(g);
                speechRecognitionEngine.SpeechRecognized += this.SreSpeechRecognized;
                speechRecognitionEngine.SpeechHypothesized += this.SreSpeechHypothesized;
                speechRecognitionEngine.SpeechRecognitionRejected += this.SreSpeechRecognitionRejected;
            }

            private void SreSpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
            {
                var said = new SaidSomethingEventArgs { Verb = Verbs.None, Matched = "?" };

                if (this.SaidSomething != null)
                {
                    this.SaidSomething(new object(), said);
                }

                Console.WriteLine("\nSpeech Rejected");
            }

            private void SreSpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
            {
                Console.Write("\rSpeech Hypothesized: \t{0}", e.Result.Text);
            }

            private void SreSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
            {
                Console.Write("\rSpeech Recognized: \t{0}", e.Result.Text);

                if ((this.SaidSomething == null) || (e.Result.Confidence < 0.3))
                {
                    return;
                }

                var said = new SaidSomethingEventArgs { Verb = 0, Phrase = e.Result.Text };


                // Look for a match in the order of the lists below, first match wins.
                List<Dictionary<string, WhatSaid>> allDicts = new List<Dictionary<string, WhatSaid>> { this.gameplayPhrases, this.singlePhrases };

                bool found = false;
                for (int i = 0; i < allDicts.Count && !found; ++i)
                {
                    foreach (var phrase in allDicts[i])
                    {
                        if (e.Result.Text.Contains(phrase.Key))
                        {
                            said.Verb = phrase.Value.Verb;

                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    return;
                }

                if (this.paused)
                {
                    // Only accept restart or reset
                    if ((said.Verb != Verbs.Resume) && (said.Verb != Verbs.Reset))
                    {
                        return;
                    }

                    this.paused = false;
                }
                else
                {
                    if (said.Verb == Verbs.Resume)
                    {
                        return;
                    }
                }

                if (said.Verb == Verbs.Pause)
                {
                    this.paused = true;
                }

                if (this.SaidSomething != null)
                {
                    this.SaidSomething(new object(), said);
                }
            }
        
            private struct WhatSaid
            {
                public Verbs Verb;
            }

            public class SaidSomethingEventArgs : EventArgs
            {
                public Verbs Verb { get; set; }

                public string Phrase { get; set; }

                public string Matched { get; set; }
            }
    }
}
