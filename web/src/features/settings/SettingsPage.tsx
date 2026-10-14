import { KeyRoundIcon, PackageIcon } from 'lucide-react';
import { Link } from 'react-router';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';

export function SettingsPage() {
  return (
    <Page>
      <PageHeader
        title="Settings"
        description="Configure printer access and slicing for this farm."
      />
      <div className="flex flex-col gap-4">
        <Link to="/settings/connection-secrets" className="block">
          <Card className="transition-colors hover:ring-primary/30">
            <CardHeader className="flex items-center gap-3">
              <KeyRoundIcon
                className="size-5 text-muted-foreground"
                aria-hidden="true"
              />
              <div>
                <CardTitle>Connection credentials</CardTitle>
                <CardDescription>
                  Manage encrypted printer access codes.
                </CardDescription>
              </div>
            </CardHeader>
          </Card>
        </Link>
        <Link to="/settings/slicing-defaults" className="block">
          <Card className="transition-colors hover:ring-primary/30">
            <CardHeader className="flex items-center gap-3">
              <PackageIcon
                className="size-5 text-muted-foreground"
                aria-hidden="true"
              />
              <div>
                <CardTitle>Slicing Defaults</CardTitle>
                <CardDescription>
                  Choose the default bundled slicer engine and version.
                </CardDescription>
              </div>
            </CardHeader>
          </Card>
        </Link>
      </div>
    </Page>
  );
}
