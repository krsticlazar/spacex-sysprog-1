namespace spacex_sysprog.Core.Interfaces;

public interface ILaunchService
{
    LaunchQueryResult QueryLaunches(LaunchQueryParameters p);
}
