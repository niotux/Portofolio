using Holodeck.Web.Computers;

namespace Holodeck.Web.Rooms;

public sealed record PlaceInput(string Library, ComputerInput? Computer = null);
