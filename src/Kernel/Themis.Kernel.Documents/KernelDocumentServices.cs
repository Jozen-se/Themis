using Microsoft.Extensions.DependencyInjection;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Documents;

/// <summary>Registers the state machine engine, the inventory adjustment commands (drafts and POST) and read, and the numbering service. Called by the composition root only.</summary>
internal static class KernelDocumentServices
{
    public static IServiceCollection AddKernelDocuments(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentStateMachine, DocumentStateMachine>();
        services.AddSingleton<INumberSeriesService, NumberSeriesService>();
        services.AddScoped<ICommandHandler<CreateInventoryAdjustment, InventoryAdjustmentView>, CreateInventoryAdjustmentHandler>();
        services.AddScoped<ICommandHandler<UpdateInventoryAdjustment, InventoryAdjustmentView>, UpdateInventoryAdjustmentHandler>();
        services.AddScoped<ICommandHandler<DiscardInventoryAdjustment, InventoryAdjustmentView>, DiscardInventoryAdjustmentHandler>();
        services.AddScoped<ICommandHandler<PostInventoryAdjustment, InventoryAdjustmentView>, PostInventoryAdjustmentHandler>();
        services.AddSingleton<ICommandValidator<CreateInventoryAdjustment>, CreateInventoryAdjustmentValidator>();
        services.AddSingleton<ICommandValidator<UpdateInventoryAdjustment>, UpdateInventoryAdjustmentValidator>();
        // Build Brief, Authorisation: a command on an existing record takes its scope from the record.
        services.AddSingleton<IScopeResolver<UpdateInventoryAdjustment>, InventoryAdjustmentScopeResolver>();
        services.AddSingleton<IScopeResolver<DiscardInventoryAdjustment>, InventoryAdjustmentScopeResolver>();
        services.AddSingleton<IScopeResolver<PostInventoryAdjustment>, InventoryAdjustmentScopeResolver>();
        services.AddScoped<IQueryHandler<GetInventoryAdjustment, InventoryAdjustmentView>, GetInventoryAdjustmentHandler>();
        return services;
    }
}
