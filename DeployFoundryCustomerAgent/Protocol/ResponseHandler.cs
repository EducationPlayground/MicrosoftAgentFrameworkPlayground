namespace DeployFoundryCustomerAgent.Protocol;

/// <summary>
/// Foundry Hosted Agent "Responses" protokolünün temel handler sınıfı.
/// Makale mimarisindeki ResponseHandler abstract class karşılığı.
/// Azure.AI.AgentServer.Responses paketi public olduğunda bu sınıf kaldırılıp
/// paketteki gerçek ResponseHandler kullanılacak.
/// </summary>
public abstract class ResponseHandler
{
    public abstract Task<ResponseResult> CreateAsync(
        CreateResponseRequest request,
        ResponseContext context,
        CancellationToken cancellationToken);
}
