#!/usr/bin/env python
"""Simple signal generator for testing transmit

Continuously output a carrier with single sideband sinusoid amplitude
modulation.

Terminate with cntl-C.
"""
import win32file
import win32pipe
import time,re,logging,threading
import argparse
import math
import signal
import time
import numpy as np
import logging
import SoapySDR
from SoapySDR import * #SOAPY_SDR_ constants
isTransmitting = 0
updateData = False
PIPEFrequency = 0.0
PIPEGAIN = 0
def processEvent(event):
    global PIPEFrequency,PIPEGAIN,updateData,isTransmitting
    PIPEFrequency = float(event[0])
    PIPEGAIN = int(event[1])
    isTransmitting = int(event[2])
    updateData = True

def startPipe():
   print('[transmission] starting PIPE')
   fileHandle = win32file.CreateFile(
    "\\\\.\\pipe\\RFtransmissionPipe", 
    win32file.GENERIC_READ | win32file.GENERIC_WRITE, 
    0, 
    None, 
    win32file.OPEN_EXISTING, 
    0, 
    None)
   while True:
        left, data = win32file.ReadFile(fileHandle, 4096)
        data = data.decode('ascii')
        data =  re.search('"(.*)"', data).group(1)
        print("[transmission] received data <-- " + (data))
        processEvent(str(data).split('@'))
        win32file.WriteFile(fileHandle,"ack".encode(), None)
def sweep_siggen(
        device,
        rate,
        ampl=1,
        freq=None,
        tx_bw=None,
        tx_chan=0,
        tx_gain=30,
        tx_ant=None,
        clock_rate=None,
        wave_freq=None,
        freq_min=920,
        freq_max=940
):
    """Generate signal until an interrupt signal is received."""

    if wave_freq is None:
        wave_freq = rate / 10

    sdr = device
    #set clock rate first
    if clock_rate is not None:
        sdr.setMasterClockRate(clock_rate)

    #set sample rate
    sdr.setSampleRate(SOAPY_SDR_TX, tx_chan, rate)

    #set bandwidth
    if tx_bw is not None:
        sdr.setBandwidth(SOAPY_SDR_TX, tx_chan, tx_bw)

    #set antenna
    if tx_ant is not None:
        sdr.setAntenna(SOAPY_SDR_TX, tx_chan, tx_ant)

    #set overall gain
    if tx_gain is not None:
        sdr.setGain(SOAPY_SDR_TX, tx_chan, tx_gain)

    #tune frontends
    if freq is not None:
        sdr.setFrequency(SOAPY_SDR_TX, tx_chan, freq)


    tx_stream = sdr.setupStream(SOAPY_SDR_TX, SOAPY_SDR_CF32, [tx_chan])
    sdr.activateStream(tx_stream)

    
    min = int(freq_min)
    max = int(freq_max)
    sdr.setFrequency(SOAPY_SDR_TX, tx_chan, min)
    while True:
            global PIPEFrequency,PIPEGAIN,updateData,isTransmitting
            if(updateData):
                sdr.deactivateStream(tx_stream)
                sdr.setFrequency(SOAPY_SDR_TX, tx_chan,PIPEFrequency)
                sdr.setGain(SOAPY_SDR_TX, tx_chan, PIPEGAIN)
                sdr.activateStream(tx_stream)
            stream_mtu = sdr.getStreamMTU(tx_stream)
            samps_chan = np.array([ampl]*stream_mtu, np.complex64)
            time_last_print = time.time()
            total_samps = 0
            phase_acc = 0
            phase_inc = 2*math.pi*wave_freq/rate
            phase_acc_next = phase_acc + stream_mtu*phase_inc
            phases = np.linspace(phase_acc, phase_acc_next, stream_mtu)
            samps_chan = ampl*np.exp(1j * phases).astype(np.complex64)
            phase_acc = phase_acc_next
            while phase_acc > math.pi * 2:
                phase_acc -= math.pi * 2
            if(isTransmitting == 1):
                status = sdr.writeStream(tx_stream, [samps_chan], samps_chan.size, timeoutUs=1000000)
                if status.ret != samps_chan.size:
                    raise Exception("Expected writeStream() to consume all samples! %d" % status.ret)
                total_samps += status.ret

            if time.time() > time_last_print + 5.0:
                rate = total_samps / (time.time() - time_last_print) / 1e6
                total_samps = 0
                time_last_print = time.time()
                
print('started transmission python binding')
sdr = SoapySDR.Device('')
threading.Thread(target=startPipe, args=()).start()
sweep_siggen(sdr,2e6,1,930e6,tx_chan=0)