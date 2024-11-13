## Simple RF Wave transmitter using SoapySDR library
a simple rf transmitter to test rf components with a GUI

![image](https://github.com/user-attachments/assets/de9c1b86-37e5-4cf5-ab27-1107face9b4b)

### Features:
Calibration - a manual calibration<br>
Transmitting methods<br>
Sweep - Hopping from Frequency range to range and Amplitude Range<br>
static - Normal static wave<br>
List - Hopping between ranges for X amount of time<br>

### how to calibrate
simply use a calibrated spectrum analyzer measure the cable attenuation between the sdr and the spectrum analyzer and take reference points <br>
example at 930M i transmit 10dB but i get -25dB i write -25dB and click Update Cal Data
do it on multiple frequencies and click finish calibration

![image](https://github.com/user-attachments/assets/6aa30cb1-55e9-4537-85b1-6f36dc0b6eed)
