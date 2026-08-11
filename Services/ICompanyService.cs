using ApsSamples.Models;

namespace ApsSamples.Services;

public interface ICompanyService
{
    Task<IReadOnlyList<CompanyInfo>> GetProjectCompaniesAsync(string projectId, string accessToken);
}
