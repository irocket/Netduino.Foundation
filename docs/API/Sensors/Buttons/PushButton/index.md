---
layout: API
title: PushButton
subtitle: Simple push button sensor.
---

# Info

The PushButton class represents a simple push button, such as a tactile momentary button. To get notified when it's clicked, subscribe to the `Clicked` event. If you need to know when the button is held down, subscribe to the `PressStarted` and `PressEnded` events.

## Sourcing

One of the most common push buttons are momentary tactile buttons and come in an array of sizes. Most commonly they have 4 leads, two of which are redundant and provide stability and mounting strength when soldered to a PCB, but some have only two leads.

* [Tactile Buttons on Amazon](https://www.amazon.com/s/ref=nb_sb_noss_1?url=search-alias%3Delectronics&field-keywords=tactile+button)
* [Tactile Buttons on Adafruit](https://www.adafruit.com/product/367)
* [Colored Tactile Buttons on SparkFun](https://www.sparkfun.com/products/10302)

![](Tactile_Switches.jpg)

## Using with Onboard Button

This class is compatible with the Onboard button, but due to a bug in the current published firmware, `(H.Cpu.Pin)0x15` must be used to address the pin:

```csharp
var pushButton = new Netduino.Foundation.Sensors.Buttons.PushButton(
    (H.Cpu.Pin)0x15, CircuitTerminationType.Floating);
```

Additionally, the `Floating` must be used for the `CircuitTerminationType`.

# Sample Circuit

The following circuit illustrates a push button wired in `CircuitTerminationType.CommonGround` configuration on digital pin `0`:

![](PushButton_bb.svg)

# API

## Properties

#### `public TimeSpan DebounceDuration { get; set; }`

This duration controls the debounce filter. It also has the effect of rate limiting clicks. Decrease this time to allow users to click more quickly.

Default time is 20 milliseconds, which should be good for most tactile push buttons.

#### `public InterruptPort DigitalIn { get; private set; }`

Returns the interrupt port that the pushbutton is configured on.

#### `public bool State`

Returns the current raw state of the switch. If the switch is pressed (connected), returns true, otherwise false.

#### `public TimeSpan LongPressThreshold { get; set; }`

The duration needed for a press to raise the `LongPress` event. By default it's half a second.

## Events

#### `public event EventHandler PressStarted`

Raised when a press starts (the button is pushed down; circuit is closed).

#### `public event EventHandler PressEnded`

Raised when a press ends (the button is released; circuit is opened).

#### `public event EventHandler Clicked`

Raised when the button circuit is re-opened after it has been closed (at the end of a "press".

#### `public event EventHandler LongPressClicked`

Raised when the button is pressed for at least the amount of time specified in the `LongPressThreshold` property.

## Constructors

#### `public PushButton(H.Cpu.Pin inputPin, CircuitTerminationType type, int debounceDuration = 20)`

Instantiates a new `PushButton` on the specified `inputPin`, with the specified [`CircuitTerminationType'](/API/CircuitTerminationType), and optionally, a specified debounce duration.