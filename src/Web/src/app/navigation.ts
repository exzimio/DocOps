import type { LucideIcon } from 'lucide-react'
import {
  ChartColumn,
  CheckSquare,
  FileText,
  GitBranch,
  Inbox,
  LayoutDashboard,
  Plug,
  Search,
  Settings,
  Sparkles,
} from 'lucide-react'
import { Permission } from '@/lib/types'

export interface NavItem {
  /** Translation key under `nav`. */
  labelKey: string
  to: string
  icon: LucideIcon
  /** Permission required to see the entry. The server enforces the same rule on every endpoint. */
  permission?: string
}

export interface NavSection {
  labelKey: string
  items: NavItem[]
}

/**
 * Navigation is data-driven: an entry only appears here once its
 * route, API and persistence exist, so the shell never contains a link that leads nowhere.
 */
export const navigationSections: NavSection[] = [
  {
    labelKey: 'nav.sectionWork',
    items: [
      { labelKey: 'nav.dashboard', to: '/app/dashboard', icon: LayoutDashboard },
      {
        labelKey: 'nav.documents',
        to: '/app/documents',
        icon: FileText,
        permission: Permission.documentsView,
      },
      {
        labelKey: 'nav.inbox',
        to: '/app/inbox',
        icon: Inbox,
        permission: Permission.inboxView,
      },
      {
        labelKey: 'nav.search',
        to: '/app/search',
        icon: Search,
        permission: Permission.searchUse,
      },
      {
        labelKey: 'nav.tasks',
        to: '/app/tasks',
        icon: CheckSquare,
        permission: Permission.tasksViewOwn,
      },
      {
        labelKey: 'nav.workflows',
        to: '/app/workflows',
        icon: GitBranch,
        permission: Permission.workflowsView,
      },
    ],
  },
  {
    labelKey: 'nav.sectionIntelligence',
    items: [
      {
        labelKey: 'nav.ai',
        to: '/app/ai',
        icon: Sparkles,
        permission: Permission.aiUse,
      },
    ],
  },
  {
    labelKey: 'nav.sectionGovernance',
    items: [
      {
        labelKey: 'nav.reports',
        to: '/app/reports',
        icon: ChartColumn,
        permission: Permission.reportsView,
      },
      {
        labelKey: 'nav.admin',
        to: '/app/admin',
        icon: Settings,
        permission: Permission.adminUsers,
      },
      {
        labelKey: 'nav.integrations',
        to: '/app/integrations',
        icon: Plug,
        permission: Permission.integrationsView,
      },
    ],
  },
]
