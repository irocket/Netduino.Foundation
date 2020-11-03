using System;
using H = Microsoft.SPOT.Hardware;
using System.Threading;
using System.Collections;
using Microsoft.SPOT;

namespace Netduino.Foundation.LEDs
{
    /// <summary>
    /// Represents a Pulse-Width-Modulation (PWM) controlled RGB LED. Controlling an RGB LED with 
    /// PWM allows for more colors to be expressed than if it were simply controlled with normal
    /// digital outputs which provide only binary control at each pin. As such, a PWM controlled 
    /// RGB LED can express millions of colors, as opposed to the 8 colors that can be expressed
    /// via binary digital output.
    /// </summary>
    public class RgbPwmLed
    {
        protected class RunningColorsConfig
        {
            public ArrayList Colors { get; set; }
            public int[] Durations { get; set; }
            public bool Loop { get; set; }
        }

        public bool IsCommonCathode { get; protected set; }
        public H.Cpu.PWMChannel RedPin { get; protected set; }
        public H.PWM RedPwm { get; protected set; }
        public H.Cpu.PWMChannel GreenPin { get; protected set; }
        public H.PWM BluePwm { get; protected set; }
        public H.Cpu.PWMChannel BluePin { get; protected set; }
        public H.PWM GreenPwm { get; protected set; }


        // TODO: this should be based on voltage drop so it can be used with or without resistors.
        protected double dutyCycleMax = 0.3; // RGB Led doesn't seem to get much brighter than at 30%

        protected float _maximumRedPwmDuty = 1;
        protected float _maximumGreenPwmDuty = 1;
        protected float _maximumBluePwmDuty = 1;
        public float RedForwardVoltage { get; protected set; }
        public float GreenForwardVoltage { get; protected set; }
        public float BlueForwardVoltage { get; protected set; }

        protected Thread _animationThread = null;
        protected bool _isRunning = false;
        protected RunningColorsConfig _runningColorConfig = null;

        /// <summary>
        /// The Color the LED has been set to.
        /// </summary>
        public Color Color
        {
            get { return _color; }
        } protected Color _color = new Color(0, 0, 0);

        /// <summary>
        /// 
        /// Implementation notes: Architecturally, it would be much cleaner to construct this class
        /// as three PwmLeds. Then each one's implementation would be self-contained. However, that
        /// would require three additional threads during ON; one contained by each PwmLed. For this
        /// reason, I'm basically duplicating the functionality for all three in here. 
        /// </summary>
        /// <param name="redPin"></param>
        /// <param name="greenPin"></param>
        /// <param name="bluePin"></param>
        /// <param name="isCommonCathode"></param>
        public RgbPwmLed(
            H.Cpu.PWMChannel redPin, H.Cpu.PWMChannel greenPin, H.Cpu.PWMChannel bluePin,
            float redLedForwardVoltage = TypicalForwardVoltage.ResistorLimited, 
            float greenLedForwardVoltage = TypicalForwardVoltage.ResistorLimited, 
            float blueLedForwardVoltage = TypicalForwardVoltage.ResistorLimited,
            bool isCommonCathode = true)
        {
            // validate and persist forward voltages
            if (redLedForwardVoltage < 0 || redLedForwardVoltage > 3.3F) {
                throw new ArgumentOutOfRangeException("redLedForwardVoltage", "error, forward voltage must be between 0, and 3.3");
            } RedForwardVoltage = redLedForwardVoltage;
            if (greenLedForwardVoltage < 0 || greenLedForwardVoltage > 3.3F) {
                throw new ArgumentOutOfRangeException("greenLedForwardVoltage", "error, forward voltage must be between 0, and 3.3");
            } GreenForwardVoltage = greenLedForwardVoltage;
            if (blueLedForwardVoltage < 0 || blueLedForwardVoltage > 3.3F) {
                throw new ArgumentOutOfRangeException("blueLedForwardVoltage", "error, forward voltage must be between 0, and 3.3");
            } BlueForwardVoltage = blueLedForwardVoltage;
            // calculate and set maximum PWM duty cycles
            _maximumRedPwmDuty = Helpers.CalculateMaximumDutyCycle(RedForwardVoltage);
            _maximumGreenPwmDuty = Helpers.CalculateMaximumDutyCycle(GreenForwardVoltage);
            _maximumBluePwmDuty = Helpers.CalculateMaximumDutyCycle(BlueForwardVoltage);

            IsCommonCathode = isCommonCathode;
            RedPin = redPin;
            GreenPin = greenPin;
            BluePin = bluePin;

            RedPwm = new H.PWM(RedPin, 100, 0, !isCommonCathode);
            GreenPwm = new H.PWM(GreenPin, 100, 0, !isCommonCathode);
            BluePwm = new H.PWM(BluePin, 100, 0, !isCommonCathode);
        }

        /// <summary>
        /// Sets the current color of the LED.
        /// </summary>
        /// 
        public void SetColor(Color color, int duration = 0)
        {
            if (duration <= 0)
            {
                _runningColorConfig = null;
                Stop();

                UpdateColor(color);
            }
            else
            {
                StartRunningColors(GetFadeConfig(_color, color, duration));
            }
        }

        void UpdateColor(Color color)
        {
            _color = color;

            // set the color based on the RGB values
            RedPwm.DutyCycle = (_color.R * _maximumRedPwmDuty);
            GreenPwm.DutyCycle = (_color.G * _maximumGreenPwmDuty);
            BluePwm.DutyCycle = (_color.B * _maximumBluePwmDuty);

            // start our PWMs.
            TurnOn();
        }

        // HACK/TODO: this is the signature I want, but it's broken until 4.4. (https://github.com/NETMF/netmf-interpreter/issues/87)
        // using arraylist for now
        //public void StartRunningColors(Color[] colors, int[] durations, bool loop)
        /// <summary>
        /// Animates through the listed colors for the specified durations. To use the same duration for all colors, 
        /// pass in an array with a length of 1 for `durations`.
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="durations"></param>
        /// <param name="loop"></param>
        /// 
        public void StartRunningColors(ArrayList colors, int[] durations, bool loop = true)
        {
            _runningColorConfig = new RunningColorsConfig()
            {
                Colors = colors,
                Durations = durations,
                Loop = loop
            };

            if (_isRunning)
            {
                Stop();
                return;
            }

            if (durations.Length != 1 && colors.Count != durations.Length)
            {
                throw new Exception("durations must either have a count of 1, if they're all the same, or colors and durations arrays must be same length.");
            }

            int count = 0;
            if(_animationThread != null)
            {
                while (_animationThread.IsAlive && count < 10)
                {
                    Thread.Sleep(100);
                    count++;
                }
            }

            if (count == 10)
                return;

            _animationThread = new Thread(() => 
            {
                while(_runningColorConfig != null)
                {
                    var nextColors = _runningColorConfig.Colors;
                    var nextDurations = _runningColorConfig.Durations;
                    var nextLoop = _runningColorConfig.Loop;
                    _runningColorConfig = null;

                    _isRunning = true;
                    AnimateColors(nextColors, nextDurations, nextLoop);
                }
            });
            _animationThread.Start();
        }

        private void StartRunningColors(RunningColorsConfig config)
        {
            StartRunningColors(config.Colors, config.Durations, config.Loop);
        }

        private void AnimateColors (ArrayList colors, int[] durations, bool loop)
        {
            while (_isRunning)
            {
                for (int i = 0; i < colors.Count; i++)
                {
                    if (_isRunning == false)
                        break;

                    UpdateColor((Color)colors[i]);
                    // if all the same, use [0], otherwise individuals
                    Thread.Sleep((durations.Length == 1) ? durations[0] : durations[i]);
                }

                if (!loop)
                    Stop();
            }
        }

        // consider removing
        public void StartAlternatingColors(Color colorOne, Color colorTwo, int colorOneDuration, int colorTwoDuration)
        {
            StartRunningColors(new ArrayList { colorOne, colorTwo }, new int[] { colorOneDuration, colorTwoDuration });
        }

        /// <summary>
        /// Start the Blink animation which sets the brightness of the LED alternating between a low and high brightness setting, using the durations provided.
        /// </summary>
        public void StartBlink(Color color, int highDuration = 200, int lowDuration = 200, float highBrightness = 1, float lowBrightness = 0)
        {
            if (highBrightness > 1 || highBrightness <= 0)
            {
                throw new ArgumentOutOfRangeException("onBrightness", "onBrightness must be > 0 and <= 1");
            }
            if (lowBrightness >= 1 || lowBrightness < 0)
            {
                throw new ArgumentOutOfRangeException("offBrightness", "lowBrightness must be >= 0 and < 1");
            }
            if (lowBrightness >= highBrightness)
            {
                throw new Exception("offBrightness must be less than onBrightness");
            }

            // pre-calculate colors
            var highColor = Color.FromHsba(color.Hue, color.Saturation, highBrightness);
            var lowColor = Color.FromHsba(color.Hue, color.Saturation, lowBrightness);

            StartRunningColors(new ArrayList { highColor, lowColor }, new int[] { highDuration, lowDuration });
        }

        /// <summary>
        /// Start the Pulse animation which gradually alternates the brightness of the LED between a low and high brightness setting, using the durations provided.
        /// </summary>
        public void StartPulse(Color color, int pulseDuration = 600, float highBrightness = 1, float lowBrightness = 0.15F)
        {
            if (highBrightness > 1 || highBrightness <= 0)
            {
                throw new ArgumentOutOfRangeException("highBrightness", "highBrightness must be between 0 and 1");
            }

            if (lowBrightness >= 1 || lowBrightness < 0)
            {
                throw new ArgumentOutOfRangeException("lowBrightness", "lowBrightness must be between 0 and 1");
            }

            if (lowBrightness >= highBrightness)
            {
                throw new Exception("lowBrightness must be less than highbrightness");
            }

            StartRunningColors(GetPulseConfig(color, pulseDuration, highBrightness, lowBrightness));
        }

        RunningColorsConfig GetFadeConfig (Color colorStart, Color colorEnd, int duration)
        {
            int interval = 60; // 60 miliseconds is probably the fastest update we want to do, given that threads are given 20 miliseconds by default. 
            int steps = duration / interval;

            var colors = new ArrayList();

            for (int i = 0; i < steps; i++)
            {
                double r = colorStart.R * (steps - i) / steps + colorEnd.R * i / steps;
                double g = colorStart.G * (steps - i) / steps + colorEnd.G * i / steps;
                double b = colorStart.B * (steps - i) / steps + colorEnd.B * i / steps;

                colors.Add(Color.FromRgb(r, g, b));
            } // walk down (start at penultimate to not repeat, and finish at 1

            colors.Add(colorEnd);    

            return new RunningColorsConfig()
            {
                Colors = colors,
                Durations = new int[] { interval },
                Loop = false
            };
        }

        RunningColorsConfig GetPulseConfig (Color color, int pulseDuration, float highBrightness, float lowBrightness)
        {
            // precalculate the colors to keep the loop tight
            int interval = 60; // 60 miliseconds is probably the fastest update we want to do, given that threads are given 20 miliseconds by default. 
            int steps = pulseDuration / interval;
            float brightnessIncrement = (highBrightness - lowBrightness) / steps;

            // array of colors we'll walk up and down
            float brightnessStep;
            var colors = new ArrayList();

            // walk up
            for (int i = 0; i < steps; i++)
            {
                brightnessStep = lowBrightness + (brightnessIncrement * i);
                colors.Add(Color.FromHsba(color.Hue, color.Saturation, brightnessStep));
            } // walk down (start at penultimate to not repeat, and finish at 1

            for (int i = steps - 2; i > 0; i--)
            {
                brightnessStep = lowBrightness + (brightnessIncrement * i);
                colors.Add(Color.FromHsba(color.Hue, color.Saturation, brightnessStep));
            }

            return new RunningColorsConfig()
            {
                Colors = colors,
                Durations = new int[] { interval },
                Loop = true
            };
        }


        /// <summary>
        /// Stops any running animations.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
        }

        /// <summary>
        /// Turns off the LED
        /// </summary>
        public void TurnOff()
        {
            RedPwm.Stop();
            GreenPwm.Stop();
            BluePwm.Stop();
        }

        /// <summary>
        /// Turns on the LED
        /// </summary>
        public void TurnOn()
        {
            RedPwm.Start();
            GreenPwm.Start();
            BluePwm.Start();
        }
    }
}
