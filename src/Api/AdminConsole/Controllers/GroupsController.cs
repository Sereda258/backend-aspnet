﻿using Bit.Api.AdminConsole.Models.Request;
using Bit.Api.AdminConsole.Models.Response;
using Bit.Api.Models.Response;
using Bit.Api.Utilities;
using Bit.Api.Vault.AuthorizationHandlers.Groups;
using Bit.Core;
using Bit.Core.AdminConsole.OrganizationFeatures.Groups.Interfaces;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.AdminConsole.Services;
using Bit.Core.Context;
using Bit.Core.Exceptions;
using Bit.Core.Repositories;
using Bit.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bit.Api.AdminConsole.Controllers;

[Route("organizations/{orgId}/groups")]
[Authorize("Application")]
public class GroupsController : Controller
{
    private readonly IGroupRepository _groupRepository;
    private readonly IGroupService _groupService;
    private readonly IDeleteGroupCommand _deleteGroupCommand;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ICurrentContext _currentContext;
    private readonly ICreateGroupCommand _createGroupCommand;
    private readonly IUpdateGroupCommand _updateGroupCommand;
    private readonly IAuthorizationService _authorizationService;
    private readonly IApplicationCacheService _applicationCacheService;
    private readonly IUserService _userService;
    private readonly IFeatureService _featureService;
    private readonly IOrganizationUserRepository _organizationUserRepository;

    public GroupsController(
        IGroupRepository groupRepository,
        IGroupService groupService,
        IOrganizationRepository organizationRepository,
        ICurrentContext currentContext,
        ICreateGroupCommand createGroupCommand,
        IUpdateGroupCommand updateGroupCommand,
        IDeleteGroupCommand deleteGroupCommand,
        IAuthorizationService authorizationService,
        IApplicationCacheService applicationCacheService,
        IUserService userService,
        IFeatureService featureService,
        IOrganizationUserRepository organizationUserRepository)
    {
        _groupRepository = groupRepository;
        _groupService = groupService;
        _organizationRepository = organizationRepository;
        _currentContext = currentContext;
        _createGroupCommand = createGroupCommand;
        _updateGroupCommand = updateGroupCommand;
        _deleteGroupCommand = deleteGroupCommand;
        _authorizationService = authorizationService;
        _applicationCacheService = applicationCacheService;
        _userService = userService;
        _featureService = featureService;
        _organizationUserRepository = organizationUserRepository;
    }

    [HttpGet("{id}")]
    public async Task<GroupResponseModel> Get(string orgId, string id)
    {
        var group = await _groupRepository.GetByIdAsync(new Guid(id));
        if (group == null || !await _currentContext.ManageGroups(group.OrganizationId))
        {
            throw new NotFoundException();
        }

        return new GroupResponseModel(group);
    }

    [HttpGet("{id}/details")]
    public async Task<GroupDetailsResponseModel> GetDetails(string orgId, string id)
    {
        var groupDetails = await _groupRepository.GetByIdWithCollectionsAsync(new Guid(id));
        if (groupDetails?.Item1 == null || !await _currentContext.ManageGroups(groupDetails.Item1.OrganizationId))
        {
            throw new NotFoundException();
        }

        return new GroupDetailsResponseModel(groupDetails.Item1, groupDetails.Item2);
    }

    [HttpGet("")]
    public async Task<ListResponseModel<GroupDetailsResponseModel>> Get(Guid orgId)
    {
        if (await FlexibleCollectionsIsEnabledAsync(orgId))
        {
            // New flexible collections logic
            return await Get_vNext(orgId);
        }

        // Old pre-flexible collections logic follows
        var canAccess = await _currentContext.ManageGroups(orgId) ||
                        await _currentContext.ViewAssignedCollections(orgId) ||
                        await _currentContext.ViewAllCollections(orgId) ||
                        await _currentContext.ManageUsers(orgId);

        if (!canAccess)
        {
            throw new NotFoundException();
        }

        var groups = await _groupRepository.GetManyWithCollectionsByOrganizationIdAsync(orgId);
        var responses = groups.Select(g => new GroupDetailsResponseModel(g.Item1, g.Item2));
        return new ListResponseModel<GroupDetailsResponseModel>(responses);
    }

    [HttpGet("{id}/users")]
    public async Task<IEnumerable<Guid>> GetUsers(string orgId, string id)
    {
        var idGuid = new Guid(id);
        var group = await _groupRepository.GetByIdAsync(idGuid);
        if (group == null || !await _currentContext.ManageGroups(group.OrganizationId))
        {
            throw new NotFoundException();
        }

        var groupIds = await _groupRepository.GetManyUserIdsByIdAsync(idGuid);
        return groupIds;
    }

    [HttpPost("")]
    public async Task<GroupResponseModel> Post(string orgId, [FromBody] GroupRequestModel model)
    {
        var orgIdGuid = new Guid(orgId);
        if (!await _currentContext.ManageGroups(orgIdGuid))
        {
            throw new NotFoundException();
        }

        var organization = await _organizationRepository.GetByIdAsync(orgIdGuid);
        var group = model.ToGroup(orgIdGuid);
        await _createGroupCommand.CreateGroupAsync(group, organization, model.Collections?.Select(c => c.ToSelectionReadOnly()).ToList(), model.Users);

        return new GroupResponseModel(group);
    }

    [HttpPut("{id}")]
    [HttpPost("{id}")]
    public async Task<GroupResponseModel> Put(Guid orgId, Guid id, [FromBody] GroupRequestModel model)
    {
        var group = await _groupRepository.GetByIdAsync(id);
        if (group == null || !await _currentContext.ManageGroups(group.OrganizationId))
        {
            throw new NotFoundException();
        }

        // Flexible Collections v1 - a user may not be able to add themselves to a group
        var orgAbility = await _applicationCacheService.GetOrganizationAbilityAsync(orgId);
        var flexibleCollectionsV1Enabled = _featureService.IsEnabled(FeatureFlagKeys.FlexibleCollectionsV1);
        if (flexibleCollectionsV1Enabled && orgAbility.FlexibleCollections && !orgAbility.AllowAdminAccessToAllCollectionItems)
        {
            var userId = _userService.GetProperUserId(User).Value;
            var organizationUser = await _organizationUserRepository.GetByOrganizationAsync(orgId, userId);
            var currentGroupUsers = await _groupRepository.GetManyUserIdsByIdAsync(id);
            if (!currentGroupUsers.Contains(organizationUser.Id) && model.Users.Contains(organizationUser.Id))
            {
                throw new BadRequestException("You cannot add yourself to groups.");
            }
        }

        var organization = await _organizationRepository.GetByIdAsync(orgId);

        await _updateGroupCommand.UpdateGroupAsync(model.ToGroup(group), organization, model.Collections?.Select(c => c.ToSelectionReadOnly()).ToList(), model.Users);
        return new GroupResponseModel(group);
    }

    [HttpDelete("{id}")]
    [HttpPost("{id}/delete")]
    public async Task Delete(string orgId, string id)
    {
        var group = await _groupRepository.GetByIdAsync(new Guid(id));
        if (group == null || !await _currentContext.ManageGroups(group.OrganizationId))
        {
            throw new NotFoundException();
        }

        await _deleteGroupCommand.DeleteAsync(group);
    }

    [HttpDelete("")]
    [HttpPost("delete")]
    public async Task BulkDelete([FromBody] GroupBulkRequestModel model)
    {
        var groups = await _groupRepository.GetManyByManyIds(model.Ids);

        foreach (var group in groups)
        {
            if (!await _currentContext.ManageGroups(group.OrganizationId))
            {
                throw new NotFoundException();
            }
        }

        await _deleteGroupCommand.DeleteManyAsync(groups);
    }

    [HttpDelete("{id}/user/{orgUserId}")]
    [HttpPost("{id}/delete-user/{orgUserId}")]
    public async Task Delete(string orgId, string id, string orgUserId)
    {
        var group = await _groupRepository.GetByIdAsync(new Guid(id));
        if (group == null || !await _currentContext.ManageGroups(group.OrganizationId))
        {
            throw new NotFoundException();
        }

        await _groupService.DeleteUserAsync(group, new Guid(orgUserId));
    }

    private async Task<ListResponseModel<GroupDetailsResponseModel>> Get_vNext(Guid orgId)
    {
        var authorized =
            (await _authorizationService.AuthorizeAsync(User, GroupOperations.ReadAll(orgId))).Succeeded;
        if (!authorized)
        {
            throw new NotFoundException();
        }

        var groups = await _groupRepository.GetManyWithCollectionsByOrganizationIdAsync(orgId);
        var responses = groups.Select(g => new GroupDetailsResponseModel(g.Item1, g.Item2));
        return new ListResponseModel<GroupDetailsResponseModel>(responses);
    }

    private async Task<bool> FlexibleCollectionsIsEnabledAsync(Guid organizationId)
    {
        var organizationAbility = await _applicationCacheService.GetOrganizationAbilityAsync(organizationId);
        return organizationAbility?.FlexibleCollections ?? false;
    }
}
