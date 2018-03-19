import { PubSubService } from '../PubSub';
import { ApiClient } from '../ApiClient';
import { AppRoot } from '../AppRoot';
import {
    GetApplicationList, ApplicationListItem,
    GetApplicationTeam, GetApplicationTeamResult, GetApplicationTeamMember,
    GetApplicationInfo, GetApplicationInfoResult
} from "../../dto/Core/Applications"

export class AppEvents {
    static readonly Created: string = "application/created";
    static readonly Removed: string = "application/removed";
    static readonly Updated: string = "application/updated";
};

export class ApplicationCreated {
    readonly id: number;
    readonly name: string;
    readonly sharedSecret: string;
    readonly appKey: string;

    constructor(id: number, name: string, sharedSecret: string, appKey: string) {
        this.id = id;
        this.name = name;
        this.sharedSecret = sharedSecret;
        this.appKey = appKey;
    }
}



export class ApplicationSummary {
    readonly id: number;
    readonly name: string;
    readonly sharedSecret: string;
    readonly appKey: string;

    constructor(id: number, name: string, sharedSecret: string, appKey: string) {
        this.id = id;
        this.name = name;
        this.sharedSecret = sharedSecret;
        this.appKey = appKey;
    }
    Team: ApplicationMember[];
}
export interface ApplicationMember {
    id: number;
    name: string;
}

export class AppLink {
    readonly id: number;
    readonly name: string;
}

export class ApplicationToCreate {
    name: string;
    sharedSecret: string;
    appKey: string;
}

//const IocService = (): ClassDecorator => {
//    return (target:Function) => {
//        console.log(Reflect.getMetadata('design:paramtypes', target));
//    };
//};

class Guid {
    static newGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }
}


//@IocService()
export class ApplicationService {
    private applications: ApplicationSummary[] = [];
    private fetchPromise: Promise<ApplicationListItem[]>;

    constructor(private pubSub: PubSubService, private apiClient: ApiClient) {
        if (!pubSub) {
            throw new Error("PubSub must be specified");
        }
        if (!apiClient) {
            throw new Error("ApiClient must be specified");
        }
    }

    async list(): Promise<ApplicationSummary[]> {
        if (this.applications.length > 0)
            return this.applications;

        // when someone else already request apps
        // so we have a pending promise.
        if (this.fetchPromise) {
            await this.fetchPromise;
            return this.applications;
        }

        var dto = new GetApplicationList();
        this.fetchPromise = this.apiClient.query<ApplicationListItem[]>(dto);;
        var result = await this.fetchPromise;

        result.forEach(app => {
            var dto = new ApplicationSummary(app.Id, app.Name, 'n/a', 'n/a');
            this.applications.push(dto);
        });
        return this.applications;
    }

    async get(appId: number): Promise<ApplicationSummary> {
        for (var i = 0; i < this.applications.length; i++) {
            if (this.applications[i].id === appId)
                return this.applications[i];
        }

        var q = new GetApplicationInfo();
        q.ApplicationId = appId;
        var result = await this.apiClient.query<GetApplicationInfoResult>(q);

        var summary: ApplicationSummary = {
            id: result.Id,
            name: result.Name,
            appKey: result.AppKey,
            sharedSecret: result.SharedSecret,
            Team: []
        };
        return summary;
    }

    async create(application: ApplicationToCreate) {
        if (application.sharedSecret == null) {
            application.sharedSecret = Guid.newGuid();
        }
        if (application.appKey == null) {
            application.appKey = Guid.newGuid();
        }

        await this.apiClient.command(application);
    }

    async getTeam(id: number): Promise<ApplicationMember[]> {
        if (id === 0) {
            throw new Error("Expected an incidentId");
        }
        
        var q = new GetApplicationTeam();
        q.ApplicationId = id;
        var result = await this.apiClient.query<GetApplicationTeamResult>(q);
        var members: ApplicationMember[] = [];
        result.Members.forEach(x => {
            members.push({
                id: x.UserId,
                name: x.UserName
            });
        });
        return members;
    }

}

