using System.Diagnostics;
using System.IO.Ports;
using System.Text;

namespace SIM800L_Flasher;

internal sealed record DeviceSnapshot(
    string Model,
    string Revision,
    string Imei,
    string Imsi,
    string Iccid,
    string OperatorName,
    string Signal,
    string Registration,
    string SimStatus,
    string PhoneNumber);

internal sealed class Sim800Device : IDisposable
{
    private readonly SemaphoreSlim _serialLock = new(1, 1);
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen == true;
    public string PortName => _port?.PortName ?? string.Empty;

    public void Open(string portName, int baudRate)
    {
        Close();
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.ASCII,
            NewLine = "\r\n",
            ReadTimeout = 250,
            WriteTimeout = 1500,
            DtrEnable = false,
            RtsEnable = false
        };
        _port.Open();
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public async Task<string> SendAsync(string command, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        if (_port?.IsOpen != true) throw new InvalidOperationException("ابتدا به ماژول متصل شوید.");
        if (!AtGuard.IsAllowed(command, out var reason)) throw new InvalidOperationException(reason);

        await _serialLock.WaitAsync(cancellationToken);
        try
        {
            _port.DiscardInBuffer();
            _port.Write(command.Trim() + "\r");

            var response = new StringBuilder();
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = _port.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    response.Append(chunk);
                    var value = response.ToString();
                    if (value.Contains("\r\nOK\r\n", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("\r\nERROR\r\n", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("+CME ERROR", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("+CMS ERROR", StringComparison.OrdinalIgnoreCase))
                        break;
                }
                await Task.Delay(30, cancellationToken);
            }

            return response.Length == 0 ? "(بدون پاسخ؛ مهلت دریافت تمام شد)" : response.ToString().Trim();
        }
        finally
        {
            _serialLock.Release();
        }
    }

    public async Task<DeviceSnapshot> ReadSnapshotAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        async Task<string> Query(string command)
        {
            progress?.Report(command);
            return Clean(command, await SendAsync(command, 3500, cancellationToken));
        }

        var ati = await Query("ATI");
        var revision = await Query("AT+GMR");
        var imei = await Query("AT+CGSN");
        var imsi = await Query("AT+CIMI");
        var iccid = await Query("AT+CCID");
        var operatorName = await Query("AT+COPS?");
        var signal = await Query("AT+CSQ");
        var registration = await Query("AT+CREG?");
        var simStatus = await Query("AT+CPIN?");
        var phoneNumber = await Query("AT+CNUM");

        return new DeviceSnapshot(ati, revision, imei, imsi, iccid, operatorName, signal, registration, simStatus, phoneNumber);
    }

    private static string Clean(string command, string response)
    {
        var lines = response.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.Equals(command, StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Equals("OK", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return lines.Length == 0 ? "—" : string.Join(" | ", lines);
    }

    public void Close()
    {
        if (_port is null) return;
        try { if (_port.IsOpen) _port.Close(); }
        finally { _port.Dispose(); _port = null; }
    }

    public void Dispose()
    {
        Close();
        _serialLock.Dispose();
    }
}
