// 1,手的位置太jupmy
// 2、截图，及其动画
// 3、player2 的问题
// 4、挥手 关闭程序
// 5、boy 到达边界会弹起来
// 6、 boy的抛物线调整


namespace Ka
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Media;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Threading;
    using Microsoft.Kinect;
    using Ka.Speech;
    using Ka.Utils;
    using System.Windows.Media;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {
        #region Private State
        //一些形状的最大和默认值
        private const int TimerResolution = 2;  // ms
        private const int NumIntraFrames = 3;
        private const int MaxShapes = 80;
        private const double MaxFramerate = 70;
        private const double MinFramerate = 15;
        private const double MinShapeSize = 12;
        private const double MaxShapeSize = 90;
        private const double DefaultDropRate = 2.5;
        private const double DefaultDropSize = 32.0;
        private const double DefaultDropGravity = 1.0;

        //
        private readonly Dictionary<int, Player> players = new Dictionary<int, Player>();//
        private readonly SoundPlayer popSound = new SoundPlayer();
        private readonly SoundPlayer squeezeSound = new SoundPlayer();

        //掉落物
        private double dropRate = DefaultDropRate;
        private double dropSize = DefaultDropSize;
        private double dropGravity = DefaultDropGravity;
        private DateTime lastFrameDrawn = DateTime.MinValue;
        private DateTime predNextFrame = DateTime.MinValue;
        private double actualFrameTime;

        //骨架
        private Skeleton[] skeletonData;
        private System.Windows.Point pointofLefthand;
        private System.Windows.Point pointofRighthand;

        // Player(s) placement in scene (z collapsed):
        private Rect playerBounds;
        private Rect screenRect;

        private double targetFramerate = MaxFramerate;
        private int frameCount;
        private bool runningGameThread;
        private FallingThings myFallingThings;
        private ThrowingThings flyingThings;
        private int playersAlive;

        private SpeechRecognizer mySpeechRecognizer;

        #endregion Private State

        #region ctor + Window Events
        public MainWindow()
        {
            InitializeComponent();
            this.RestoreWindowState();
        }

        // Since the timer resolution defaults to about 10ms precisely, we need to
        // increase the resolution to get framerates above between 50fps with any
        // consistency.
        [DllImport("Winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern int TimeBeginPeriod(uint period);

        private void RestoreWindowState()
        {
            // Restore window state to that last used
            Rect bounds = Properties.Settings.Default.PrevWinPosition;
            if (bounds.Right != bounds.Left)
            {
                this.Top = bounds.Top;
                this.Left = bounds.Left;
                this.Height = bounds.Height;
                this.Width = bounds.Width;
            }

            this.WindowState = (WindowState)Properties.Settings.Default.WindowState;
        }

        private void WindowLoaded(object sender, EventArgs e)
        {
            playfield.ClipToBounds = true;//playfield是canvas

            this.myFallingThings = new FallingThings(MaxShapes, this.targetFramerate, NumIntraFrames);
            this.flyingThings = new ThrowingThings(myFallingThings, this.targetFramerate, NumIntraFrames);

            this.UpdatePlayfieldSize();

            this.flyingThings.SetGravity(this.dropGravity);
            this.flyingThings.SetDropRate(this.dropRate);

            this.myFallingThings.SetGravity(this.dropGravity);
            this.myFallingThings.SetDropRate(this.dropRate);
            this.myFallingThings.SetSize(this.dropSize);
            this.myFallingThings.SetPolies(PolyType.All);
            this.myFallingThings.SetGameMode(GameMode.Off);

            SensorChooser.KinectSensorChanged += this.SensorChooserKinectSensorChanged;

            this.popSound.Stream = Properties.Resources.Pop_5;

            this.popSound.Play();

            TimeBeginPeriod(TimerResolution);
            var myGameThread = new Thread(this.GameThread);
            myGameThread.SetApartmentState(ApartmentState.STA);
            myGameThread.Start();

            Text.NewText(this.screenRect.Width / 30, new Point(this.screenRect.Width / 2, this.screenRect.Height / 2), "Shapes!");
        }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            this.runningGameThread = false;
            Properties.Settings.Default.PrevWinPosition = this.RestoreBounds;
            Properties.Settings.Default.WindowState = (int)this.WindowState;
            Properties.Settings.Default.Save();
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            SensorChooser.Kinect = null;
        }

        #endregion ctor + Window Events

        #region Kinect discovery + setup

        private void SensorChooserKinectSensorChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue != null)
            {
                this.UninitializeKinectServices((KinectSensor)e.OldValue);
            }

            // Only enable this checkbox if we have a sensor
            //enableAec.IsEnabled = e.NewValue != null;

            if (e.NewValue != null)
            {
                this.InitializeKinectServices((KinectSensor)e.NewValue);
            }
        }

        // Kinect enabled apps should customize which Kinect services it initializes here.
        private KinectSensor InitializeKinectServices(KinectSensor sensor)
        {
            // Application should enable all streams first.
            sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);

            sensor.SkeletonFrameReady += this.SkeletonsReady;
            /*
                Correction – Takes a float ranging from 0 to 1.0. The lower the number, the more
                correction is applied.
                • JitterRadius – Sets the radius of correction. If a joint position “jitters” outside of
                the set radius, it is corrected to be at the radius. The property is a float value
                measured in meters
                • MaxDeviationRadius – Used this setting in conjunction with the JitterRadius
                setting to determine the outer bounds of the jitter radius. Any point that falls
                outside of this radius is not considered a jitter, but a valid new position. The
                property is a float value measured in meters.
                • Prediction – Returns the number of frames predicted.
                • Smoothing – Determines the amount of smoothing applied while processing
                skeletal frames. It is a float type with a range of 0 to 1.0. The higher the value, the
                more smoothing applied. A zero value does not alter the skeleton data. */
            sensor.SkeletonStream.Enable(new TransformSmoothParameters()
            {
                Smoothing = 0.5f,
                Correction = 0.3f,
                Prediction = 0.5f,
                JitterRadius = 0.05f,
                MaxDeviationRadius = 1f
            });

            try
            {
                sensor.Start();
            }
            catch (IOException)
            {
                SensorChooser.AppConflictOccurred();
                return null;
            }

            // Start speech recognizer after KinectSensor.Start() is called
            // returns null if problem with speech prereqs or instantiation.
            this.mySpeechRecognizer = SpeechRecognizer.Create();
            this.mySpeechRecognizer.SaidSomething += this.RecognizerSaidSomething;
            this.mySpeechRecognizer.Start(sensor.AudioSource);
            //enableAec.Visibility = Visibility.Visible;
            // this.UpdateEchoCancellation(this.enableAec);

            return sensor;
        }

        // Kinect enabled apps should uninitialize all Kinect services that were initialized in InitializeKinectServices() here.
        private void UninitializeKinectServices(KinectSensor sensor)
        {
            sensor.Stop();

            sensor.SkeletonFrameReady -= this.SkeletonsReady;

            if (this.mySpeechRecognizer != null)
            {
                this.mySpeechRecognizer.Stop();
                this.mySpeechRecognizer.SaidSomething -= this.RecognizerSaidSomething;
                this.mySpeechRecognizer.Dispose();
                this.mySpeechRecognizer = null;
            }

            //enableAec.Visibility = Visibility.Collapsed;
        }

        #endregion Kinect discovery + setup

        #region Kinect Skeleton processing
        private void SkeletonsReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    int skeletonSlot = 0;

                    if ((this.skeletonData == null) || (this.skeletonData.Length != skeletonFrame.SkeletonArrayLength))
                    {
                        this.skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];//给骨架数据申请适当空间
                    }

                    skeletonFrame.CopySkeletonDataTo(this.skeletonData);

                    foreach (Skeleton skeleton in this.skeletonData)
                    {
                        if (SkeletonTrackingState.Tracked == skeleton.TrackingState)
                        {
                            Player player;
                            if (this.players.ContainsKey(skeletonSlot))
                            {
                                player = this.players[skeletonSlot];
                            }
                            else
                            {
                                player = new Player(skeletonSlot);
                                player.SetBounds(this.playerBounds);
                                this.players.Add(skeletonSlot, player);
                            }

                            player.LastUpdated = DateTime.Now;

                            // Update player's bone and joint positions
                            if (skeleton.Joints.Count > 0)
                            {
                                player.IsAlive = true;
                                
                                // hand track
                                this.pointofLefthand = player.UpdateJointPosition(skeleton.Joints, JointType.HandLeft, ActualWidth, ActualHeight);
                                this.pointofRighthand = player.UpdateJointPosition(skeleton.Joints, JointType.HandRight, ActualWidth, ActualHeight);

                                player.TrackHand(this.pointofLefthand, skeleton.Joints[JointType.HandLeft], LeftHand, true);
                                player.TrackHand(this.pointofRighthand, skeleton.Joints[JointType.HandRight], RightHand, false);

                                //hand shack test
                                if (skeleton.Joints[JointType.ElbowRight].Position.Y - skeleton.Joints[JointType.HandRight].Position.Y>0.5)
                                {
                                    if(player.ShackHandCheck(skeleton.Joints[JointType.HandRight].Position , skeleton.Joints[JointType.ElbowRight].Position ))
                                        this.Close();
                                }

                            }
                        }

                        if (skeletonSlot<1)
                        skeletonSlot++;
                    }
                }
            }
        }

        private void CheckPlayers()
        {
            foreach (var player in this.players)
            {
                if (!player.Value.IsAlive)
                {
                    // Player left scene since we aren't tracking it anymore, so remove from dictionary
                    this.players.Remove(player.Value.GetId());
                    break;
                }
            }

            // Count alive players
            int alive = this.players.Count(player => player.Value.IsAlive);

            //   **************** 待修改*******************

            if (alive != 0)
            {
                alive = 1;
            }
            //   **************** 待修改*******************
            if (alive != this.playersAlive)
            {
                if (alive == 2)
                {
                    this.myFallingThings.SetGameMode(GameMode.TwoPlayer);
                }
                else if (alive == 1)
                {
                    this.myFallingThings.SetGameMode(GameMode.Solo);
                }
                else if (alive == 0)
                {
                    this.myFallingThings.SetGameMode(GameMode.Off);
                }

                if ((this.playersAlive == 0) && (this.mySpeechRecognizer != null))
                {
                    BannerText.NewBanner(
                        Properties.Resources.Vocabulary,
                        this.screenRect,
                        true,
                        System.Windows.Media.Color.FromArgb(200, 255, 255, 255));
                }

                this.playersAlive = alive;
            }
        }

        private void PlayfieldSizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.UpdatePlayfieldSize();
        }

        private void UpdatePlayfieldSize()
        {
            // Size of player wrt size of playfield, putting ourselves low on the screen.
            this.screenRect.X = 0;
            this.screenRect.Y = 0;
            this.screenRect.Width = this.playfield.ActualWidth;
            this.screenRect.Height = this.playfield.ActualHeight;

            BannerText.UpdateBounds(this.screenRect);

            this.playerBounds.X = 0;
            this.playerBounds.Width = this.playfield.ActualWidth;
            this.playerBounds.Y = this.playfield.ActualHeight * 0.2;
            this.playerBounds.Height = this.playfield.ActualHeight * 0.75;

            foreach (var player in this.players)
            {
                player.Value.SetBounds(this.playerBounds);
            }

            Rect fallingBounds = this.playerBounds;
            fallingBounds.Y = 0;
            fallingBounds.Height = playfield.ActualHeight;
            if (this.myFallingThings != null)
            {
                this.myFallingThings.SetBoundaries(fallingBounds);
            }
        }
        #endregion Kinect Skeleton processing

        #region GameTimer/Thread
        private void GameThread()
        {
            this.runningGameThread = true;
            this.predNextFrame = DateTime.Now;
            this.actualFrameTime = 1000.0 / this.targetFramerate;

            // Try to dispatch at as constant of a framerate as possible by sleeping just enough since
            // the last time we dispatched.
            while (this.runningGameThread)
            {
                // Calculate average framerate.  
                DateTime now = DateTime.Now;
                if (this.lastFrameDrawn == DateTime.MinValue)
                {
                    this.lastFrameDrawn = now;
                }

                double ms = now.Subtract(this.lastFrameDrawn).TotalMilliseconds;
                this.actualFrameTime = (this.actualFrameTime * 0.95) + (0.05 * ms);
                this.lastFrameDrawn = now;

                // Adjust target framerate down if we're not achieving that rate
                this.frameCount++;
                if ((this.frameCount % 100 == 0) && (1000.0 / this.actualFrameTime < this.targetFramerate * 0.92))
                {
                    this.targetFramerate = Math.Max(MinFramerate, (this.targetFramerate + (1000.0 / this.actualFrameTime)) / 2);
                }

                if (now > this.predNextFrame)
                {
                    this.predNextFrame = now;
                }
                else
                {
                    double milliseconds = this.predNextFrame.Subtract(now).TotalMilliseconds;
                    if (milliseconds >= TimerResolution)
                    {
                        Thread.Sleep((int)(milliseconds + 0.5));
                    }
                }

                this.predNextFrame += TimeSpan.FromMilliseconds(1000.0 / this.targetFramerate);

                this.Dispatcher.Invoke(DispatcherPriority.Send, new Action<int>(this.HandleGameTimer), 0);
            }
        }

        private void HandleGameTimer(int param)
        {
            // Every so often, notify what our actual framerate is
            if ((this.frameCount % 100) == 0)
            {
                this.myFallingThings.SetFramerate(1000.0 / this.actualFrameTime);
                this.flyingThings.SetFramerate(1000.0 / this.actualFrameTime);
            }

            // Advance animations, and do hit testing.
            for (int i = 0; i < NumIntraFrames; ++i)
            {
                foreach (var pair in this.players)
                {
                    HitType hit = this.myFallingThings.LookForHits(pair.Value.Segments, pair.Value.GetId());
                }

                this.myFallingThings.AdvanceFrame();
                this.flyingThings.AdvanceFrame();
            }

            // Draw new Wpf scene by adding all objects to canvas
            playfield.Children.Clear();
            this.myFallingThings.DrawFrame(this.playfield.Children);
            this.flyingThings.DrawFrame(this.playfield.Children);

            BannerText.Draw(playfield.Children);
            Text.Draw(playfield.Children);

            this.CheckPlayers();
        }
        #endregion GameTimer/Thread

        #region Kinect Speech processing
        private void RecognizerSaidSomething(object sender, SpeechRecognizer.SaidSomethingEventArgs e)
        {
            Text.NewText(this.screenRect.Width / 30, new Point(this.screenRect.Width / 2, this.screenRect.Height / 2), e.Matched);
            switch (e.Verb)
            {
                case SpeechRecognizer.Verbs.Pause:
                    this.myFallingThings.SetDropRate(0);
                    this.myFallingThings.SetGravity(0);
                    break;
                case SpeechRecognizer.Verbs.Resume:
                    this.myFallingThings.SetDropRate(this.dropRate);
                    this.myFallingThings.SetGravity(this.dropGravity);
                    break;
                case SpeechRecognizer.Verbs.Reset:
                     
                    /*this.dropRate = DefaultDropRate;
                     this.dropSize = DefaultDropSize;
                     this.dropGravity = DefaultDropGravity;
                     this.myFallingThings.SetPolies(PolyType.All);
                     this.myFallingThings.SetDropRate(this.dropRate);
                     this.myFallingThings.SetGravity(this.dropGravity);
                     this.myFallingThings.SetSize(this.dropSize);
                     this.myFallingThings.SetShapesColor(System.Windows.Media.Color.FromRgb(0, 0, 0), true);
                     this.myFallingThings.Reset();
                     */
                    foreach (var pair in this.players)
                     {
                         this.myFallingThings.LookForBound(flyingThings, this.pointofLefthand, this.pointofRighthand, pair.Value.GetId());//player的id获取有待改进 
                     }
                    //Text.NewText(this.screenRect.Width / 30, new Point(this.screenRect.Width / 2, this.screenRect.Height / 2), "Shapes!");
                    break;
                case SpeechRecognizer.Verbs.Ka:
                    // player.UpdateJointPosition(skeleton.Joints, JointType.HandLeft,ActualWidth,ActualHeight)                 
                    foreach (var pair in this.players)
                    {
                        this.myFallingThings.LookForBound(flyingThings, this.pointofLefthand, this.pointofRighthand, pair.Value.GetId());//player的id获取有待改进 
                    }
                    //Text.NewText(this.screenRect.Width / 30, new Point(this.screenRect.Width / 2, this.screenRect.Height / 2), "Shapes!");
                    break;
                case SpeechRecognizer.Verbs.More:
                    this.dropRate *= 1.5;
                    this.myFallingThings.SetDropRate(this.dropRate);
                    break;
                case SpeechRecognizer.Verbs.Fewer:
                    this.dropRate /= 1.5;
                    this.myFallingThings.SetDropRate(this.dropRate);
                    break;
                case SpeechRecognizer.Verbs.Bigger:
                    this.dropSize *= 1.5;
                    if (this.dropSize > MaxShapeSize)
                    {
                        this.dropSize = MaxShapeSize;
                    }

                    this.myFallingThings.SetSize(this.dropSize);
                    break;
                case SpeechRecognizer.Verbs.Biggest:
                    this.dropSize = MaxShapeSize;
                    this.myFallingThings.SetSize(this.dropSize);
                    break;
                case SpeechRecognizer.Verbs.Smaller:
                    this.dropSize /= 1.5;
                    if (this.dropSize < MinShapeSize)
                    {
                        this.dropSize = MinShapeSize;
                    }

                    this.myFallingThings.SetSize(this.dropSize);
                    break;
                case SpeechRecognizer.Verbs.Smallest:
                    this.dropSize = MinShapeSize;
                    this.myFallingThings.SetSize(this.dropSize);
                    break;
                case SpeechRecognizer.Verbs.Faster:
                    this.dropGravity *= 1.25;
                    if (this.dropGravity > 4.0)
                    {
                        this.dropGravity = 4.0;
                    }

                    this.myFallingThings.SetGravity(this.dropGravity);
                    break;
                case SpeechRecognizer.Verbs.Slower:
                    this.dropGravity /= 1.25;
                    if (this.dropGravity < 0.25)
                    {
                        this.dropGravity = 0.25;
                    }

                    this.myFallingThings.SetGravity(this.dropGravity);
                    break;
            }
        }

        private void EnableAecChecked(object sender, RoutedEventArgs e)
        {
            CheckBox enableAecCheckBox = (CheckBox)sender;
            this.UpdateEchoCancellation(enableAecCheckBox);
        }

        private void UpdateEchoCancellation(CheckBox aecCheckBox)
        {
            this.mySpeechRecognizer.EchoCancellationMode = aecCheckBox.IsChecked != null && aecCheckBox.IsChecked.Value
                ? EchoCancellationMode.CancellationAndSuppression
                : EchoCancellationMode.None;
        }

        #endregion Kinect Speech processing

    }
}
