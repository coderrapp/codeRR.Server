import * as MenuApi from "../../services/menu/MenuApi";
import Vue from 'vue';
import { Component, Watch } from 'vue-property-decorator';

interface IRouteNavigation {
    routeName: string;
    url: string;
    setMenu(name: String): void;
}
type NavigationCallback = (context: IRouteNavigation) => void;

@Component
export default class DiscoverMenuComponent extends Vue {
    private callbacks: NavigationCallback[] = [];

    childMenu: MenuApi.MenuItem[] = [];
    currentApplicationId: number | null = null;

    @Watch('$route.params.applicationId')
    onApplicationSelected(value: string, oldValue: string) {
        if (!value) {
            this.currentApplicationId = null;
            return;
        }

        if (this.$route.fullPath.indexOf('/discover/') === -1) {
            return;
        }

        var applicationId = parseInt(value);
        this.currentApplicationId = applicationId;
    }

    mounted() {
        if (!this.$route.params.applicationId) {
            return;
        }

        var appId = parseInt(this.$route.params.applicationId);
        this.currentApplicationId = appId;
    }
}
