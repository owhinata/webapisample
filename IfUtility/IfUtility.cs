using System.Net.Sockets;
using System.Text.Json;

namespace IfUtilityLib;

public class IfUtility
{
    private TcpClient? _tcpClient;
    
    // Virtual methods allow test subclasses to intercept calls without interfaces
    public virtual void HandleStart(string json)
    {
        // Parse JSON and connect to TCP server if address and port are provided
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("address", out var addressProp) && 
                root.TryGetProperty("port", out var portProp))
            {
                var address = addressProp.GetString();
                var port = portProp.GetInt32();
                
                ConnectToTcpServer(address!, port);
            }
        }
        catch (Exception ex)
        {
            // Log or handle connection error
            Console.WriteLine($"Failed to connect to TCP server: {ex.Message}");
        }
    }

    public virtual void HandleEnd(string json)
    {
        // Disconnect from TCP server if connected
        DisconnectFromTcpServer();
    }
    
    protected virtual void ConnectToTcpServer(string address, int port)
    {
        try
        {
            DisconnectFromTcpServer(); // Ensure any existing connection is closed
            
            _tcpClient = new TcpClient();
            _tcpClient.Connect(address, port);
            Console.WriteLine($"Connected to TCP server at {address}:{port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TCP connection failed: {ex.Message}");
            _tcpClient?.Dispose();
            _tcpClient = null;
        }
    }
    
    protected virtual void DisconnectFromTcpServer()
    {
        if (_tcpClient != null)
        {
            try
            {
                _tcpClient.Close();
                Console.WriteLine("Disconnected from TCP server");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disconnecting: {ex.Message}");
            }
            finally
            {
                _tcpClient.Dispose();
                _tcpClient = null;
            }
        }
    }
    
    public bool IsConnected => _tcpClient?.Connected ?? false;
}

