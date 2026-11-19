namespace Holodeck.Web.Computers;

// An inbound port: the agent listens on ContainerPort; it is published on 127.0.0.1:HostPort.
public sealed record PortGrant(int ContainerPort, int HostPort);
