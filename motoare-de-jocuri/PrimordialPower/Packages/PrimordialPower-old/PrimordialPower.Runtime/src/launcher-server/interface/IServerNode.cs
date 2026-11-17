namespace PrimordialPower.Runtime;

public interface IServerNode : IServer
{
  IServer[] Servers { get; }
}



public interface IServerNode<TService> : IServerNode
{
  TService Services { get; }
}