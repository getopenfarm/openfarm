import { KeyRoundIcon } from 'lucide-react';
import { Link } from 'react-router';
import {
  Card,
  CardContent,
  CardDescription,
  CardTitle,
} from '@/components/ui/card';

export function SettingsPage() {
  return (
    <div className="flex flex-col gap-4">
      <Link to="/settings/connection-secrets" className="block">
        <Card className="transition-colors hover:bg-accent">
          <CardContent className="flex items-center gap-3">
            <KeyRoundIcon
              className="size-5 text-muted-foreground"
              aria-hidden="true"
            />
            <div>
              <CardTitle>Connection Secrets</CardTitle>
              <CardDescription>
                Manage encrypted printer access codes.
              </CardDescription>
            </div>
          </CardContent>
        </Card>
      </Link>
    </div>
  );
}
