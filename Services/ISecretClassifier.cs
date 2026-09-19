using SharpML2.Models;

namespace SharpML2.Services;

public interface ISecretClassifier
{
    Task<ClassificationResult> ClassifyAsync(CandidateSecret candidate, CancellationToken cancellationToken);
}
