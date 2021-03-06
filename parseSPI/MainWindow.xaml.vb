﻿Imports System.Windows.Forms  'requires System.Windows.Forms reference
Imports System.IO
Imports System.Xml
Imports System.Threading
Imports System.ComponentModel

''' <summary>
''' SPI Sniffing Microcontroller Protocol Parser by Robert Parker (c)2015
''' See https://github.com/pyblendnet-js/parseSPI
''' Uses the output captured by version 3.0 of Bus Pirate hardware and BusPirate.SPIsniffer.v0.3 executeable.
''' Please see http://dangerousprototypes.com/docs/Bus_Pirate
''' Output captured either with BusPirate.SPIsniffer.v0.3 executeable using console  command SPIsniffer -d COM9 -r 1 > raw.txt
''' Or with RealTermBusPirateSniff.exe (see https://github.com/pyblendnet-js/RealTermBusPirateSniff )
''' </summary>
''' <remarks></remarks>

Class MainWindow

    Private Sub windowLoaded()
        instrLbl.Content = "BK2421.xml"
        loadInstructionSet(instrLbl.Content)
    End Sub

    Private Sub instrClick(ByVal obj As Object, ByVal e As EventArgs)
        ' Create OpenFileDialog
        Dim dlg As New Microsoft.Win32.OpenFileDialog()
        ' Set filter for file extension and default file extension
        dlg.FileName = instrLbl.Content
        dlg.DefaultExt = "*.xml"
        dlg.Filter = "Instruction set files (*.xml) | *.xml;..."
        'dlg.InitialDirectory = util.addDiskPrefix(util.calcRelativePaths(imgPathTextBox.Content), "C:")
        ' Display OpenFileDialog by calling ShowDialog method
        Dim result As Nullable(Of Boolean) = dlg.ShowDialog()

        ' Get the selected file name and display in a TextBox
        If result = True Then
            ' Open document
            Dim fid As String = dlg.FileName
            If loadInstructionSet(fid) Then
                spiData.Text = ""
                instrLbl.Content = fid
            End If
        End If
    End Sub

    Private Sub browseClick(ByVal obj As Object, ByVal e As EventArgs)
        If scanWorker IsNot Nothing Then
            If scanWorker.IsBusy Then
                scanWorker.CancelAsync()
                scanWorker = Nothing
                browseBtn.Content = "Browse"
                Return
            End If
        End If
        ' Create OpenFileDialog
        Dim dlg As New Microsoft.Win32.OpenFileDialog()
        ' Set filter for file extension and default file extension
        dlg.DefaultExt = "*.txt"
        dlg.Filter = "Source files (*.txt) | *.txt;..."
        'dlg.InitialDirectory = util.addDiskPrefix(util.calcRelativePaths(imgPathTextBox.Content), "C:")
        ' Display OpenFileDialog by calling ShowDialog method
        Dim result As Nullable(Of Boolean) = dlg.ShowDialog()

        ' Get the selected file name and display in a TextBox
        If result = True Then
            ' Open document
            Dim fid As String = dlg.FileName
            sourceLbl.Content = fid
            Select Case typCombo.SelectedIndex
                Case 0            'buspirate binary spi
                    convertBusPirateSnifferSource(fid)
                Case 1            'buspirate binary spi
                    convertRealTermHexSource(fid)
            End Select
            saveAsBtn.IsEnabled = True
        End If
    End Sub

    Private Sub convertBusPirateSnifferSource(ByVal fid As String)
        Dim data As String() = File.ReadAllLines(fid)
        Dim startData As Boolean = False
        For Each l As String In data
            If Not startData Then   'skip all the status messages
                If l.EndsWith("CKE=1OK") Then  'this is actual 2 messages = CKE=1 clock positive edge and OK
                    startData = True
                End If
            Else
                Dim pairs As String() = l.Split(" ")
                Dim singles As New List(Of String)
                For i As Integer = 0 To pairs.Count Step 2
                    Dim b As String = pairs(i)
                    singles.Add(b)
                Next
                hexToInstructions(singles.ToArray())
            End If
        Next
    End Sub

    Private Sub convertRealTermHexSource(ByVal fid As String)
        Dim data As String = File.ReadAllText(fid)
        Dim singles As New List(Of String)
        'build hex pairs
        For i As Integer = 0 To data.Length - 1 Step 2
            Dim b As String = data(i) & data(i + 1)
            singles.Add(b)
        Next
        hexToInstructions(singles.ToArray())
    End Sub

    Dim scanPos As Integer = 0
    Dim scanBuffer As String()
    Private scanWorker As BackgroundWorker = Nothing
    Dim dataSync As Boolean = False
    Dim packet As Boolean = False
    Dim MOSI As Boolean = False
    Dim MISO As Boolean = False
    Dim packet_count As Integer = 0
    Dim master_bytes As New List(Of Byte)
    Dim slave_bytes As New List(Of Byte)
    Dim text_pos As Integer  'used to tab spi parse output
    Dim pmb As Byte() = {}
    Dim psb As Byte() = {}
    Dim packetText As String = ""
    Dim packetRepeat As Integer = 0
    Dim replyRepeat As Integer = 0
    Dim lastCmdReply As String = ""
    Dim scanText As String = ""
    Dim showPackets As Boolean
    Dim left_over_master_bytes As New List(Of Byte)
    Dim left_over_slave_bytes As New List(Of Byte)

    Private Sub hexToInstructions(ByVal hexcode As String())
        showPackets = showPacketsChkBox.IsChecked
        scanBuffer = hexcode
        scanPos = 0
        packetRepeat = 0
        replyRepeat = 0
        scanText = ""
        spiData.Text = ""
        dataSync = False
        MOSI = False
        MISO = False
        scanWorker = New BackgroundWorker()
        scanWorker.WorkerReportsProgress = True
        scanWorker.WorkerSupportsCancellation = True
        AddHandler scanWorker.RunWorkerCompleted, AddressOf scanFinished
        AddHandler scanWorker.ProgressChanged, AddressOf scanProgress
        AddHandler scanWorker.DoWork, AddressOf scanTick
        scanWorker.RunWorkerAsync()
        browseBtn.Content = "Cancel"
    End Sub

    Private Sub scanTick()
        Dim percent As Integer = 0
        For i As Integer = 0 To scanBuffer.Count - 1
            Dim pc As Integer = i * 100 \ scanBuffer.Count
            If pc <> percent Then
                percent = pc
                If scanWorker IsNot Nothing Then
                    scanWorker.ReportProgress(percent)
                End If
                Thread.Sleep(100)
            End If
            Dim b As String = scanBuffer(i)
            'Try
            If Not dataSync Then
                If b = "01" Then
                    scanText = "Sync" & vbLf
                    dataSync = True
                Else
                    scanText &= b & vbLf
                End If
            Else
                If MOSI Then
                    packetText &= b & " ("
                    master_bytes.Add(Byte.Parse(b, Globalization.NumberStyles.HexNumber))
                    MOSI = False
                    MISO = True  'next byte is MISO
                ElseIf MISO Then
                    packetText &= b & ") "
                    packet_count += 1
                    If packet_count >= 16 Then
                        packet_count = 0
                        packetText &= vbLf
                    End If
                    slave_bytes.Add(Byte.Parse(b, Globalization.NumberStyles.HexNumber))
                    MISO = False
                Else
                    Select Case b
                        Case "5B"
                            'packetText &= "Packet Start" & vbLf
                            text_pos = scanText.Length
                            packet = True
                            master_bytes = left_over_master_bytes
                            slave_bytes = left_over_slave_bytes
                        Case "5C"
                            MOSI = True  'next byte is mosi
                        Case "5D"
                            'While (scanText.Length < text_pos + 40)
                            'scanText &= " "
                            'End While
                            Dim mb As Byte() = master_bytes.ToArray()
                            Dim sb As Byte() = slave_bytes.ToArray()
                            Dim same_packet As Boolean = False
                            If mb.Length = pmb.Length And mb.Length = sb.Length Then
                                same_packet = True
                                For mi As Integer = 0 To mb.Length - 1
                                    If mb(mi) <> pmb(mi) Or sb(mi) <> psb(mi) Then
                                        same_packet = False
                                    End If
                                Next
                            End If
                            If showPackets And same_packet Then
                                packetRepeat += 1
                            Else
                                If showPackets Then
                                    If packetRepeat > 0 Then
                                        scanText &= "Last pack repeated " & packetRepeat.ToString() & vbLf
                                        packetRepeat = 0
                                    End If
                                    scanText &= packetText
                                End If
                                Dim rply As String = parseSpiPacket(mb, sb)
                                If rply.EndsWith("unexpected end of packet") Then
                                    Dim rs As String() = rply.Split(" ")
                                    Dim pos_packet As Integer = Integer.Parse(rs(rs.Count - 5))
                                    left_over_master_bytes = master_bytes.GetRange(pos_packet, master_bytes.Count - pos_packet)
                                    left_over_slave_bytes = slave_bytes.GetRange(pos_packet, slave_bytes.Count - pos_packet)
                                    Dim pos_string As Integer = Integer.Parse(rs(rs.Count - 6))
                                    rply = rply.Substring(0, pos_string)
                                Else
                                    left_over_master_bytes.Clear()
                                    left_over_slave_bytes.Clear()
                                End If
                                If showPackets Then
                                    scanText &= rply & vbLf
                                Else
                                    Dim cmd_replies As String() = rply.Split(vbLf)
                                    For Each cr As String In cmd_replies
                                        If cr.Length = 0 Or cr = vbTab Then
                                            Continue For
                                        End If
                                        If Not showPackets And cr = lastCmdReply Then
                                            replyRepeat += 1
                                        Else
                                            If replyRepeat > 0 Then
                                                scanText &= " Last line repeated " & replyRepeat & " times " & vbLf
                                            End If
                                            replyRepeat = 0
                                            scanText &= cr & vbLf
                                            lastCmdReply = cr
                                        End If
                                        ' need to be able to join incomplete packets and re parse
                                        'scanText &= vbLf ' & "Packet End" & vbLf
                                        packetRepeat = 0
                                    Next
                                End If
                            End If
                            pmb = mb
                            psb = sb
                            packet = False
                            packet_count = 0
                            packetText = ""
                    End Select
                End If
            End If
            'Catch ex As Exception
            ' scanText &= "Exception:" & ex.ToString() & vbLf & "  parsing byte " & i & " = " & scanBuffer(i)
            ' scanText &= vbLf
            ' End Try
        Next
        If packetRepeat > 0 Then
            scanText &= "Last pack repeated " & packetRepeat.ToString() & vbLf
        End If
        If replyRepeat > 0 Then
            scanText &= " Last line repeated " & replyRepeat.ToString() & " times " & vbLf
        End If
    End Sub

    Private Function parseSpiPacket(ByVal mb As Byte(), ByVal sb As Byte()) As String
        If mb.Count <> sb.Count Then
            MsgBox("How can the slave bytes be different to the master bytes")
        End If
        If instructions Is Nothing Then
            Return ""
        End If
        Dim offset As Integer = 0
        Dim start_of_command As Integer
        Dim str_cmd_pos As Integer
        Dim human As String = ""
        While offset < mb.Count
            human &= vbLf & vbTab
            Dim match_found As Boolean = False
            start_of_command = offset
            str_cmd_pos = human.Length
            For Each cmd As instructionClass In instructions
                For i As Integer = 0 To cmd.Binary.Count - 1
                    Dim m As Byte = mb(offset + i)
                    If cmd.Mask IsNot Nothing Then
                        Dim mi = i - (cmd.Binary.Count - cmd.Mask.Count)
                        If mi >= 0 And mi < cmd.Mask.Count Then
                            m = m And Not cmd.Mask(mi)
                        End If
                        'Else
                        '    Console.WriteLine()
                    End If
                    If m <> cmd.Binary(i) Then  'doesn't match this byte
                        Exit For
                    End If
                    If i = cmd.Binary.Count - 1 Then  'full match
                        match_found = True
                        human &= cmd.Name
                        Dim loc As locClass = Nothing
                        If cmd.fields IsNot Nothing Then
                            human &= "("
                            For Each f As fieldClass In cmd.fields
                                If Not human.EndsWith("(") Then
                                    human &= ","
                                End If
                                human &= f.Name & "="   'field name
                                Dim mf As UInt64 = 0
                                Dim ff As UInt64 = 0
                                If f.Mask Is Nothing Then
                                    errorMsg("Command " & cmd.Name & " Field error", "Field " & f.Name & " has no bit mask")
                                    Return "FIELD ERROR"
                                End If
                                For fi As Integer = 0 To f.Mask.Count - 1
                                    mf = mf << 8
                                    Dim fmi = i - (cmd.Binary.Count - cmd.Mask.Count)
                                    If fmi >= 0 And fmi < f.Mask.Count Then
                                        mf = mf Or (mb(offset + fmi) And f.Mask(fi))
                                        ff = ff Or f.Mask(fi)
                                    End If
                                Next
                                While (ff And 1) = 0
                                    ff = ff >> 1
                                    mf = mf >> 1
                                End While
                                'mf is the loc address
                                If f.Map IsNot Nothing Then  'which map to lookup loc
                                    Dim map_name As String = f.Map.ToLower()
                                    If Not regMaps.Keys.Contains(map_name) Then  'map is not recognised
                                        If variables.Keys.Contains(map_name) Then  'see if this is a variable
                                            map_name = variables(map_name).value.ToLower()   'and if so use it's value
                                        End If
                                    End If
                                    If regMaps.Keys.Contains(map_name) Then  'map is recognised
                                        Dim map As mapClass = regMaps(map_name)
                                        If map.locs.Keys.Contains(mf) Then
                                            loc = map.locs(mf)
                                        End If
                                    End If
                                ElseIf f.Variable.Length > 0 Then
                                    variables(f.Variable).Number = mf
                                    human &= f.Variable & "="  'lines below will add value
                                End If
                                If loc Is Nothing Then
                                    human &= mf.ToString()   'no map or no loc in map so just show address
                                Else
                                    human &= loc.Name
                                End If
                            Next
                            human &= ")"
                        End If
                        Dim bytes As Integer = 0
                        If loc IsNot Nothing Then
                            If loc.VariableBytes.Length > 0 Then
                                If variables.Keys.Contains(loc.VariableBytes) Then
                                    bytes = variables(loc.VariableBytes).Number
                                Else
                                    errorMsg("VariableBytes", "Value for variable bytes not set in variable " & loc.VariableBytes)
                                End If
                            Else
                                If cmd.minReply > 0 And loc.ReadBytes > 0 Then
                                    bytes = loc.ReadBytes  'readbytes overrides 
                                Else
                                    bytes = loc.Bytes
                                End If
                            End If
                        End If

                        If cmd.minReply > 0 Then
                            Dim reply As String = ""
                            If bytes = 0 Then
                                If cmd.bytes.Length > 0 Then
                                    If variables.Keys.Contains(cmd.bytes) Then
                                        bytes = variables(cmd.bytes).Number
                                    Else
                                        While True
                                            Dim nm As String = InputBox("Enter number of bytes for " & cmd.bytes)
                                            Try
                                                bytes = Integer.Parse(nm)
                                                Exit While
                                            Catch ex As Exception
                                                MsgBox("Must be a number between:" & cmd.minSend & " and " & cmd.maxSend)
                                            End Try
                                        End While
                                        variables(cmd.bytes) = New variableClass
                                        variables(cmd.bytes).Number = bytes
                                    End If
                                Else
                                    bytes = cmd.minReply  'so use command minimum reply
                                End If
                            End If
                            If cmd.maxReply > 0 And bytes > cmd.maxReply Then
                                errorMsg("ExceedMaxReply", "Bytes exceed max reply bytes for " & cmd.Name)
                            End If
                            For j As Integer = 0 To bytes - 1
                                Dim oi As Integer = offset + cmd.Binary.Count
                                If oi >= mb.Count Then
                                    human &= " " & str_cmd_pos & " " & start_of_command & " unexpected end of packet"
                                    Return human
                                End If
                                reply = printHex(sb(oi)) + reply
                                offset += 1
                            Next
                            human &= "R= 0x" & reply
                            If loc IsNot Nothing Then
                                If loc.bits IsNot Nothing Then
                                    If loc.bits.Count > 0 Then
                                        human &= parseBits(loc.bits, reply)
                                    End If
                                End If
                            End If
                        ElseIf cmd.minSend > 0 Then   'this is a write style command
                            If bytes = 0 Then  'number of bytes has not been set by the register location
                                If cmd.bytes.Length > 0 Then
                                    If variables.Keys.Contains(cmd.bytes) Then
                                        bytes = variables(cmd.bytes).Number
                                    Else
                                        While True
                                            Dim nm As String = InputBox("Enter number of bytes for " & cmd.bytes)
                                            Try
                                                bytes = Integer.Parse(nm)
                                                Exit While
                                            Catch ex As Exception
                                                MsgBox("Must be a number between:" & cmd.minSend & " and " & cmd.maxSend)
                                            End Try
                                        End While
                                        variables(cmd.bytes) = New variableClass
                                        variables(cmd.bytes).Number = bytes
                                    End If
                                Else
                                    bytes = cmd.minSend  'so use command minimum send
                                End If
                            End If
                            If cmd.maxSend > 0 And bytes > cmd.maxSend Then
                                errorMsg("ExceedMaxSend", "Bytes exceed max send bytes for " & cmd.Name)
                            End If
                            'gather the values being sent to the slave into an unsigned long number
                            Dim send_bytes As New List(Of Byte)
                            Dim send_hex As String = ""
                            For j As Integer = 0 To bytes - 1
                                Dim oi As Integer = offset + cmd.Binary.Count
                                If oi >= mb.Count Then
                                    human &= " " & str_cmd_pos & " " & start_of_command & " unexpected end of packet"
                                    Return human
                                End If
                                send_bytes.Add(mb(oi))
                                send_hex = printHex(mb(oi)) & send_hex
                                offset += 1
                            Next
                            'if this is a register of interest the instruction descriptions may had bit fields to examine
                            If loc IsNot Nothing Then
                                If loc.bits IsNot Nothing Then
                                    For Each b As bitClass In loc.bits
                                        'gather the bit field mask into one unsigned long value
                                        Dim bit_mask As UInt64 = 0
                                        Dim v As UInt64 = 0
                                        Dim bj As Integer = 0
                                        For bi As Integer = b.Mask.Count - 1 To 0 Step -1  'lsb first
                                            If b.Mask(bi) = 0 Then
                                                Continue For
                                            End If
                                            bit_mask *= 256
                                            bit_mask += b.Mask(bi)
                                            'extract the bit field value from the value being sent to the slave
                                            v *= 256
                                            v += send_bytes(bi) And b.Mask(bi)
                                        Next
                                        'and shift it down to base 1
                                        While (bit_mask And 1) = 0
                                            bit_mask = bit_mask >> 1
                                            v = v >> 1
                                        End While
                                        'does this bit field have a variable to set
                                        If b.Variable.Length > 0 Then
                                            'should the bit field value be remapped
                                            If b.Remap IsNot Nothing Then
                                                Try
                                                    v = b.Remap(v)
                                                Catch
                                                    errorMsg("BitRemap", "Could not remap variable for bits " & b.Name & " of loc " & loc.Name & " with command " & cmd.Name)
                                                End Try
                                            End If
                                            'set the variable with the value extracted
                                            setVariableNumber(b.Variable, v)
                                        End If
                                    Next
                                End If
                            End If
                            'show the value being sent
                            human &= "W= 0x" & send_hex
                            If loc IsNot Nothing Then
                                If loc.bits IsNot Nothing Then
                                    If loc.bits.Count > 0 Then
                                        human &= parseBits(loc.bits, send_hex)
                                    End If
                                End If
                            End If
                            End If
                            'does this command have a script to activate
                            If cmd.script.Length > 0 Then
                                Try
                                    Dim svs As String() = cmd.script.Split()
                                    Dim si As Integer = 0
                                    While si < svs.Count
                                        Select Case svs(si).ToLower()
                                            Case "toggle"  'toggle the following variable between two preset values
                                                'this command references a variable
                                                si += 1
                                                Dim vi = svs(si)
                                                Dim v = variables(vi)
                                                If v.value = v.values(0) Then
                                                    v.value = v.values(1)
                                                Else
                                                    v.value = v.values(0)
                                                End If
                                        End Select
                                        si += 1
                                    End While
                                Catch
                                    errorMsg("Script Error", "Error in using script:" & cmd.script)
                                End Try
                            End If
                            offset += cmd.Binary.Count
                            Exit For
                            'Else
                            '    Console.WriteLine("2nd byte")
                    End If
                Next
                If match_found Then
                    Exit For
                End If
            Next
            If Not match_found Then
                human &= "Unknown_command"
                For i As Integer = offset To mb.Count - 1
                    human &= " " & Hex(mb(i))
                Next
                Exit While
            End If
        End While
        Return human
    End Function

    Private Function printHex(ByVal nr As Byte)
        Dim h As String = Hex(nr)
        If h.Length < 2 Then
            h = "0" & h
        End If
        Return h
    End Function

    Private Function buildMask(ByVal ms As Byte()) As UInt64
        Dim mask As UInt64 = 0
        For mi As Integer = 0 To ms.Count - 1
            If mi > 8 Then
                Return mask
            End If
            mask = mask Or ms(mi) << (8 * mi)
        Next
        Return mask
    End Function

    Private Function getFieldValue(ByVal nr As UInt64, ByVal ms As Byte()) As UInt64
        Dim mask As Long = buildMask(ms)
        Dim v As UInt64 = nr And mask
        While (mask And 1) = 0
            v = v >> 1
            mask = mask >> 1
        End While
        Return v
    End Function

    Private Function parseBits(ByVal bits As bitClass(), ByVal h As String)
        Dim nr = UInt64.Parse(h, Globalization.NumberStyles.HexNumber)
        Dim rtn As String = " ["
        For Each b As bitClass In bits
            Dim v As UInt64 = getFieldValue(nr, b.Mask)
            rtn &= b.Name & "=" & v & ","
        Next
        rtn = rtn.Substring(0, rtn.Length - 1) & "]"
        Return rtn
    End Function

    Class variableClass
        Public name As String
        Public value As String = ""
        Public values As String() = Nothing
        Public Number As UInt64 = 0
    End Class

    Private variables As New Dictionary(Of String, variableClass)

    Private Sub setVariableNumber(ByVal name As String, ByVal number As UInt64)
        If Not variables.Keys.Contains(name) Then
            Dim var As New variableClass
            var.name = name
            variables(name) = var
        End If
        variables(name).Number = number
    End Sub

    Class fieldClass
        Public Name As String
        Public Mask As Byte()
        Public Map As String
        Public Variable As String = ""
    End Class

    Class instructionClass
        Public Name As String
        Public Binary As Byte()
        Public Mask As Byte()
        Public fields As fieldClass() = Nothing
        Public minReply As Integer = 0
        Public maxReply As Integer = 0
        Public minSend As Integer = 0
        Public maxSend As Integer = 0
        Public bytes As String = ""  'can be a variable to set the number of bytes
        Public script As String = ""
    End Class

    Class bitClass
        Public Name As String
        Public Mask As Byte() = Nothing
        Public Variable As String = ""
        Public Remap As Integer() = Nothing
    End Class

    Class locClass
        Public Name As String
        Public Address As Integer
        Public Bytes As Integer = 1
        Public ReadBytes As Integer = 0  'if > 0 then this is the read size overiding Bytes
        Public VariableBytes As String = ""
        Public bits As bitClass() = Nothing
    End Class

    Class mapClass
        Public Name As String
        Public locs As Dictionary(Of Integer, locClass) = Nothing
    End Class

    Private instructions As instructionClass() = Nothing
    Private regMaps As New Dictionary(Of String, mapClass)

    Private Function loadInstructionSet(ByVal fid As String) As Boolean
        Dim xDoc = New XmlDocument()
        'Try
        xDoc.Load(fid)
        Dim head = xDoc.DocumentElement
        'Console.WriteLine(head.ToString)
        If head.Name <> "instructions" Then
            Return False
        End If
        variables.Clear()
        regMaps.Clear()
        Dim attrs = head.Attributes
        'read attributes
        'Dim reply = attributes.GetNamedItem("base_path")
        Dim var_list = head.GetElementsByTagName("variable")
        For Each var_value As XmlElement In var_list
            Dim var As New variableClass()
            For Each attr As XmlAttribute In var_value.Attributes
                Select Case attr.Name.ToLower()
                    Case "mnemonic", "name"
                        var.name = attr.Value
                    Case "value"
                        var.value = attr.Value
                    Case "values"
                        var.values = attr.Value.Split(",")
                    Case "number"
                        var.Number = UInt64.Parse(attr.Value)
                End Select
            Next
            variables(var.name) = var
        Next
        Dim cmd_list = head.GetElementsByTagName("command")
        Dim commands As New List(Of instructionClass)
        For Each command_value As XmlElement In cmd_list
            Dim cmd As New instructionClass()
            For Each attr As XmlAttribute In command_value.Attributes
                Select Case attr.Name.ToLower()
                    Case "mnemonic", "name"
                        cmd.Name = attr.Value
                    Case "binary"
                        cmd.Binary = parseHexList(attr.Value)
                    Case "mask"
                        cmd.Mask = parseHexList(attr.Value)
                    Case "minreply"
                        cmd.minReply = Integer.Parse(attr.Value)
                    Case "maxreply"
                        cmd.maxReply = Integer.Parse(attr.Value)
                    Case "minsend"
                        cmd.minSend = Integer.Parse(attr.Value)
                    Case "maxsend"
                        cmd.maxSend = Integer.Parse(attr.Value)
                    Case "bytes"
                        cmd.bytes = attr.Value  'can be a variable
                    Case "script"
                        cmd.script = attr.Value
                End Select
            Next
            Dim field_list = command_value.GetElementsByTagName("field")
            Dim fields As New List(Of fieldClass)
            For Each field_value As XmlElement In field_list
                Dim field As New fieldClass()
                For Each attr As XmlAttribute In field_value.Attributes
                    Select Case attr.Name.ToLower()
                        Case "mnemonic", "name"
                            field.Name = attr.Value
                        Case "mask"
                            field.Mask = parseHexList(attr.Value)
                        Case "map"
                            field.Map = attr.Value
                        Case "variable"
                            field.Variable = attr.Value
                    End Select
                Next
                fields.Add(field)
            Next
            If fields.Count > 0 Then
                cmd.fields = fields.ToArray()
            End If
            commands.Add(cmd)
        Next
        If commands.Count > 0 Then
            instructions = commands.ToArray()
        End If
        Dim map_list = head.GetElementsByTagName("map")
        Dim maps As New List(Of mapClass)
        For Each map_value As XmlElement In map_list
            Dim map As New mapClass()
            For Each attr As XmlAttribute In map_value.Attributes
                Select Case attr.Name.ToLower()
                    Case "name"
                        map.Name = attr.Value
                End Select
            Next
            Dim loc_list = map_value.GetElementsByTagName("loc")
            Dim locs As New Dictionary(Of Integer, locClass)
            For Each loc_value As XmlElement In loc_list
                Dim loc As New locClass()
                For Each attr As XmlAttribute In loc_value.Attributes
                    Select Case attr.Name.ToLower()
                        Case "mnemonic", "name"
                            loc.Name = attr.Value
                        Case "address"
                            loc.Address = Integer.Parse(attr.Value, Globalization.NumberStyles.HexNumber)
                        Case "bytes"
                            Try
                                loc.Bytes = Integer.Parse(attr.Value)
                            Catch
                                loc.VariableBytes = attr.Value
                            End Try
                        Case "readbytes"
                            loc.ReadBytes = Integer.Parse(attr.Value)
                    End Select
                Next
                Dim bit_list = loc_value.GetElementsByTagName("bit")
                Dim bits As New List(Of bitClass)
                For Each bit_value As XmlElement In bit_list
                    Dim bit As New bitClass()
                    For Each attr As XmlAttribute In bit_value.Attributes
                        Select Case attr.Name.ToLower()
                            Case "mnemonic", "name"
                                bit.Name = attr.Value
                            Case "mask"
                                bit.Mask = parseHexList(attr.Value)
                            Case "variable"
                                bit.Variable = attr.Value
                            Case "remap"
                                Dim vs As String() = attr.Value.Split(",")
                                Dim ls As New List(Of Integer)
                                For Each v As String In vs
                                    ls.Add(Integer.Parse(v))
                                Next
                                bit.Remap = ls.ToArray()
                        End Select
                    Next
                    bits.Add(bit)
                Next
                If bits.Count > 0 Then
                    loc.bits = bits.ToArray()
                End If
                locs(loc.Address) = loc
            Next
            If locs.Keys.Count > 0 Then
                map.locs = locs
            End If
            regMaps(map.Name.ToLower()) = map
        Next
        Return True
    End Function

    Private Function parseHexList(ByVal str As String) As Byte()
        Dim vs As String() = str.Split(",")
        Dim ls As New List(Of Byte)
        For Each v In vs
            ls.Add(Byte.Parse(v, Globalization.NumberStyles.HexNumber))
        Next
        Return ls.ToArray()
    End Function

    Private blockErrors As New List(Of String)

    Private Sub errorMsg(ByVal typ As String, ByVal msg As String)
        If blockErrors.Contains(typ) Then  'ignor this typ
            Return
        End If
        If MsgBox(msg & vbLf & vbTab & "Show this error again?", MsgBoxStyle.YesNo) = MsgBoxResult.No Then
            blockErrors.Add(typ)
        End If
    End Sub

    Private Sub saveAsClick(ByVal obj As Object, ByVal e As EventArgs)
        ' Create OpenFileDialog
        Dim dlg As New Microsoft.Win32.SaveFileDialog()
        ' Set filter for file extension and default file extension
        dlg.DefaultExt = "*.txt"
        dlg.Filter = "Save As (*.txt) | *.txt;..."
        'dlg.InitialDirectory = util.addDiskPrefix(util.calcRelativePaths(imgPathTextBox.Content), "C:")
        ' Display OpenFileDialog by calling ShowDialog method
        Dim result As Nullable(Of Boolean) = dlg.ShowDialog()

        ' Get the selected file name and display in a TextBox
        If result = True Then
            ' Open document
            Dim fid As String = dlg.FileName
            File.WriteAllText(fid, spiData.Text)
        End If
    End Sub

    Private Sub sourceTypeChange()
        'not required but here for ease of adding feature
    End Sub

    Private Sub scanProgress(ByVal sender As Object, ByVal e As ProgressChangedEventArgs)
        progressBar.Width = Me.ActualWidth * e.ProgressPercentage / 100.0
        progressLbl.Content = e.ProgressPercentage.ToString() + "%"
        spiData.Text = scanText
    End Sub

    Private Sub scanFinished(ByVal sender As Object, ByVal e As RunWorkerCompletedEventArgs)
        saveAsBtn.IsEnabled = True
        browseBtn.Content = "Browse"
        progressBar.Width = 0
        progressLbl.Content = "100%"
        spiData.Text = scanText
    End Sub

End Class