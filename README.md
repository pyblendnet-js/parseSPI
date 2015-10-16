<html>
<body>
<h1>parseSPI</h1>
<p>Converts raw hex code from SPI bus sniffing into slave register settings and data flow.</p>
<img src="ScreenShot.png">
<h2>Introduction</h2>
<p>The hex decimal codes obtains from sniffing the SPI bus between a microcontroller and a slave device need to
be interpreted against the register settings for the slave device.  This is often complex, tedious and often erroneous.
This program uses the coded instruction set transposed into an xml file to interpret the raw code seen on the SPI bus.
This program was written in visual basic using a WPF GUI, since this is what I work with every day.</p>
<h2>History</h2>
<p>I purchased two MJX twin rotor helicopters which are large enough to carry some electronics and cheap enough to be expendable.  My intention is to interface a Texas Instruments Tiva microcontroller to accept SPI command arriving on the
transceiver board and retransmit altered SPI data to the helicopters controller board.</p>
<p>Articles such as <a href="http://dzlsevilgeniuslair.blogspot.dk/2013/11/more-toy-quadcopter-hacking.html">
Controlling toy quadcopter(s) with Arduino </a> showed that this might be even easier than I had thought - or at least not impossible.</p>
<p>The transceiver turned out to be a BK2421 (see xml file in debug directory) and I measured the SPI clock rate at only 100KHz.  This was within the ability of the <a href="http://dangerousprototypes.com/docs/Bus_Pirate">Bus Pirate</a> which can sniff to 10Mhz.  I purchased the V3 SparkFun version from OceanControls and have been very pleased with it - I hope SparkFun are generous with their contributions to it's developers.</p>
<p>So far I have been able to sniff and interpret the intialisation sequence, the binding process and the operation of 4 of the 5 channels.  Though transceiver operation is still a bit of a mystery, I am hopeful to be able to insert a microcontroller to intercept the transmissions and so give these helicopters some drone abilities.</p>
<h2>Installation and Description</h2>
<p>To use the code, only the contents of the debug directory are required.</p>
<p>Run the parseSPI.exe.  The BK2421 is the only instruction set at the moment and is selected by default.
   There are two types of source types that program will accept:</p>
   <ul>
     <li><b>BusPirate.SPIsniffer.v0.3</b> - Originally I used the Bus Pirate binary sniffer (unzips as BusPirate.SPIsniffer.v0.3) with the console command "SPIsniffer -d COM9 -r 1 > raw.txt" and this was okay to catch the initialisation sequence in 6kB before the buffers were over run due to the slow 115200 baud rate on the USB connection.  The output from this sniffer was strangely echoed, so this parser program only looks at every second byte.</li>
     <li><b>RealTerm</b> - following the guidelines from Homens on <a href="http://dangerousprototypes.com/forum/viewtopic.php?f=4&t=6765&p=59413&hilit=SPIsniffer#p59413">this</a> forum entry, I discovered that I could use a visual basic program to open Real Term,
     change the USB baudrate to 800KHz and use RealTerm to capture the data continuously to file.
     In this case, there is no echoing and only a 0x01 byte at the start to begin the normal BusPirate protocol. You can find my visual basic project to automate the capture <a href="http://github.com/pyblendnet-js/RealTermBusPirateSniff">here</a>.</li>
  </ul>
<p>So, select the source type from RealTerm RawHex (the default) or BusPirate.SPIsniffer.v0.3 and then browse to the captured file.  Parsing will commence immediately but can be cancelled.  A button at the bottom will then be enabled allowing you to save the output.</p>
<p>If you wished to see the raw data packets imbedded within the parsed text, there is a check box to be selected before the source is chosen.</p>
<h2>Instruction Sets</h2>
<p>See the BK2421.xml file for an example of how to generate an instruction set for a different slave device, or to fix error sin this one.  The following description should help to understand my framework.</p>
<p>There are three main entries:</p>
<ui>
<li>COMMAND = a sequence of bytes which come from the MOSI bus line.</li>
<li>VARIABLE = a value which may change during the parsing of the code.</li>
<li>MAP = a register map for the slave device.</li>
</ul>
<h3>Command Item</h3>
<p>As an example:</p>
<pre><i><b>&lt;command Mnemonic="R_REGISTER" Binary="00" Mask="1F" MinReply="1" MaxReply="5">
    <field Name="REGISTER ADDRESS" Mask="1F" Map="register_bank"/>
  </command></b></i></pre>
<p>Attributes:</p>
<ul>
<li>Mnemonic or Name - output to the parsed data.
<li>Binary - a comma separated list of hex byte values to match against bits in the sniffed command to test.</li>
<li>Mask - a comma separated list of hex byte values to exclude from the Binary bits.</li>
<li>MinReply - the minimum number of bytes that the slave should return.  Also used as the actual number of the
bytes if no other setting is made such as from register size. When greater than 0 tells parser that this is a
read command. Data to register is prefixed with <i><b>R=</b></i></li>
<li>MaxReply - only used for testing that nothing has gone seriously wrong.</li>
<li>MinSend - as per reply but for writing to the slave. When greater than 0 tells parser that this is a
write command. Data to register is prefixed with <i><b>W=</b></i></li>
</ul>
<p>Commands can have an optional series of field(s).
I have only entered the fields I am interest in or those that are necessary to set the variables for parsing.</p>
<p>Field attributes are:</p>
<ul>
<li>Name - name for this field.  Not currently output to screen.</li>
<li>Mask - a comma separated list of hex bytes that define the bits for this field.</li>
<li>Map - if this field gives the address for a register, which map to use to find the register location.
This can be a variable name as it is in this case as the BK2421 has two register banks that are selected by special commands.</li>
<li>Script - a special action required to continue parsing.  See example below.</li>
</ul>
<p>Three more examples:</p>
<p><pre><i><b>
&lt;command Mnemonic="REUSE_TX_PL" Binary="E3"/>
&lt;command Mnemonic="ACTIVATE1" Binary="50,73" />
&lt;command Mnemonic="ACTIVATE2" Binary="50,53" Script="toggle register_bank"/>
</b></i></pre></p>
<p>The first is a singe byte command with no data, the second is a two byte command with no data and the third is a two byte command with no data that triggers a change in the register bank being used.  This script contains the command toggle which changes the variable register_bank between the two possible values.</p>
<h3>Variable Item</h3>
<p>An example:</p>
<p><pre><i><b>  &lt;variable Name="register_bank" value="Register_Bank1" values="Register_Bank1,Register_Bank2"/>
</b></i></pre></p>
<p>This defines a variable by the name of "register_bank" to have an initial value of "Register_Bank1" and two possible values.</p>
<p>Another variable is used for the BK2421 and is implied when it appears in a regsiter location bit field as will be seen below.</p>
<h3>Map Item</h3>
<p>A map is typically a list of register locations in the slave SPI device.</p>
<p>An example:</p>
<p><pre><i><b>
&lt;loc Mnemonic="SETUP_AW" Address="03" >
  &lt;bit Mnemonic="AW" Mask="03" Variable="reg_width" Remap="0,3,4,5"/>
&lt;/loc>
</b></i></pre></p>
<p>This is the SETUP_AW register in Bank1 on the BK2421. Attributes are:</p>
<ul>
<li>Mnemonic or Name - this is what is shown in the parsed text.</li>
<li>Address - the value used to identify the register from the MOSI codes.</i>
<li>Bytes - unless stated this defaults to 1 but can also be set to a variable as the BK2421 has some registers that can have a width of 3,4 or 5 bytes. This was set using the SETUP_AW register given in the example.</li>
<li>ReadBytes - the BK2421 has one status register that is read (but not written) from bank 1 regardless of whether bank 1 or 2 are selected.  In order for the system to parse correctly I have limited the bank2 register to always read as 1 byte long even though it is 4 bytes for a write.  As such the parsed data is not strickly correct but at least remains in byte synchronisation and I may return to fix this.
<ul>
<p>Optionally the register loc block can have bit fields as shown. These have attributes:</p>
<ul>
<li>Mnemonic or Name - not currently used.</li>
<li>Mask - which bits of the register apply.</li>
<li>Variable - optional variable for the value found in the command to set this register.  Used in this example to set the number of bytes in registers with variable width.</li>
<li>Remap - an optional comma seperated list of values.  The value is then used as an index into this list to derive the required variable value.</p>
</ul>
<p>This parsing rules have been derived only to work for the BK2421 initialisation sequence and will evolve as I need further functionality and maybe add other devices.</p>
<p>It has already proven worth by showing that while the data sheet says that the FLUSH_RX and FLUSH_TX commands are one byte commands, the data sniffed from a working system and the example code from the manufacturers website, both show that a second byte is used.</p>
</body>
</html>
