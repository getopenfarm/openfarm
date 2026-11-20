import { zodResolver } from '@hookform/resolvers/zod';
import {
  ArrowRightIcon,
  ChevronDownIcon,
  CircleAlertIcon,
  EyeIcon,
  EyeOffIcon,
  LockIcon,
  UserIcon,
} from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { apiUrl } from '@/lib/api/apiUrl';
import { useOpenFarmApiEndpointsAuthLoginEndpoint } from '@/lib/api/generated/auth/auth';
import { localReturnTo, useLoginProviders } from '@/lib/auth/oidc';
import { useAuth } from '@/lib/auth/useAuth';

const loginSchema = z.object({
  username: z.string().min(1, 'Username is required'),
  password: z.string().min(1, 'Password is required'),
});

type LoginFormValues = z.infer<typeof loginSchema>;

const inputClass =
  'h-[46px] rounded-[10px] bg-card pl-10 text-[15px] focus-visible:border-primary focus-visible:ring-primary/20 md:text-[15px] dark:bg-card';
const leadIconClass =
  'pointer-events-none absolute top-1/2 left-3.5 size-[17px] -translate-y-1/2 text-muted-foreground';

export function LoginPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [showPassword, setShowPassword] = useState(false);
  const providers = useLoginProviders();
  const sso = providers.data?.oidcEnabled === true;
  const internalOn = providers.data?.internalAuthenticationEnabled ?? true;
  // An error counts as settled, so password sign-in stays reachable.
  const ready = !!providers.data || providers.isError;
  const params = new URLSearchParams(window.location.search);
  const ssoError = params.get('ssoError');
  const localOnly = sso && !internalOn;
  const providerDown = ssoError === 'provider_configuration';
  const defaultOpen =
    !sso ||
    (!!ssoError && internalOn) ||
    (localOnly && (providerDown || params.get('local') === '1'));
  // Null until the visitor toggles, so the default follows the providers once they load.
  const [toggled, setOpen] = useState<boolean | null>(null);
  const open = toggled ?? defaultOpen;
  const methodName = internalOn ? 'username and password' : 'local sign-in';
  const trigger = (
    <CollapsibleTrigger asChild>
      <Button
        type="button"
        variant="ghost"
        size={internalOn ? 'default' : 'sm'}
        className={`group w-full text-muted-foreground aria-expanded:bg-transparent aria-expanded:text-muted-foreground ${internalOn ? '' : 'mt-2 text-xs'}`}
      >
        {open
          ? `Hide ${methodName}`
          : internalOn
            ? 'Use username and password'
            : 'Local sign-in'}
        {(internalOn || open) && (
          <ChevronDownIcon
            data-icon="inline-end"
            className="transition-transform group-data-[state=open]:rotate-180"
          />
        )}
      </Button>
    </CollapsibleTrigger>
  );
  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { username: '', password: '' },
  });

  const loginMutation = useOpenFarmApiEndpointsAuthLoginEndpoint({
    mutation: {
      onSuccess: (response) => {
        const token = response.data.token;
        if (!token) {
          toast.error('Sign-in failed. No token returned.');
          return;
        }
        auth.login(token);
        const target = new URLSearchParams(window.location.search).get(
          'returnTo',
        );
        navigate(localReturnTo(target), { replace: true });
      },
    },
  });
  const failed = loginMutation.isError;

  function onSubmit(values: LoginFormValues) {
    loginMutation.mutate({ data: values });
  }

  return (
    <div className="flex min-h-svh items-center justify-center bg-background px-6 py-10">
      <div className="w-full max-w-[368px]">
        <img
          src={`${import.meta.env.BASE_URL}favicon.svg`}
          alt="OpenFarm"
          className="mx-auto mb-6 size-[54px] drop-shadow-[0_4px_10px_rgb(21_96_245/0.35)]"
        />
        <div className="mb-6 text-center">
          <h1 className="text-[23px] font-semibold tracking-[-0.022em]">
            Welcome back
          </h1>
          <p className="mt-[7px] text-sm/normal text-muted-foreground">
            Sign in to continue.
          </p>
        </div>
        {ssoError && (
          <p role="alert" className="mb-4 text-sm text-destructive">
            {ssoError === 'provider_configuration'
              ? 'SSO is unavailable. Ask an administrator to check the issuer URL, the provider connection, and the server log.'
              : ssoError === 'unavailable'
                ? 'This sign-in cannot access OpenFarm. Contact an administrator.'
                : 'SSO sign-in failed. Try again, or contact an administrator.'}
          </p>
        )}
        {providers.isError && (
          <p role="alert" className="mb-4 text-sm">
            Load failed.{' '}
            <button
              type="button"
              className="underline"
              onClick={() => void providers.refetch()}
            >
              Retry
            </button>
          </p>
        )}
        {sso && (
          <Button asChild className="mb-4 w-full">
            <a
              href={apiUrl(
                `/api/v1/auth/sso/login?returnTo=${encodeURIComponent(localReturnTo(params.get('returnTo')))}`,
              )}
            >
              Sign in with {providers.data?.displayName}
            </a>
          </Button>
        )}
        {ready && (
          <Collapsible open={open} onOpenChange={setOpen}>
            {sso && internalOn && trigger}
            <CollapsibleContent
              className={
                sso
                  ? 'data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:slide-in-from-top-1'
                  : undefined
              }
            >
              {localOnly && (
                <p className="mb-3 text-center text-sm text-muted-foreground">
                  {providerDown
                    ? 'SSO is unavailable. Administrators can sign in with a local account.'
                    : 'Local administrator sign-in'}
                </p>
              )}
              <Form {...form}>
                <form
                  onSubmit={form.handleSubmit(onSubmit)}
                  className="flex flex-col gap-[15px]"
                >
                  <FormField
                    control={form.control}
                    name="username"
                    render={({ field, fieldState }) => (
                      <FormItem>
                        <FormLabel className="text-[13px]">Username</FormLabel>
                        <div className="relative">
                          <UserIcon className={leadIconClass} aria-hidden />
                          <FormControl>
                            <Input
                              autoComplete="username"
                              placeholder="Username"
                              className={inputClass}
                              aria-invalid={failed || !!fieldState.error}
                              {...field}
                            />
                          </FormControl>
                        </div>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={form.control}
                    name="password"
                    render={({ field, fieldState }) => (
                      <FormItem>
                        <FormLabel className="text-[13px]">Password</FormLabel>
                        <div className="relative">
                          <LockIcon className={leadIconClass} aria-hidden />
                          <FormControl>
                            <Input
                              type={showPassword ? 'text' : 'password'}
                              autoComplete="current-password"
                              placeholder="Password"
                              className={`${inputClass} pr-11`}
                              aria-invalid={failed || !!fieldState.error}
                              {...field}
                            />
                          </FormControl>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="absolute top-1/2 right-1.5 size-[34px] -translate-y-1/2 text-muted-foreground"
                            aria-label={
                              showPassword ? 'Hide password' : 'Show password'
                            }
                            onClick={() => setShowPassword((shown) => !shown)}
                          >
                            {showPassword ? <EyeOffIcon /> : <EyeIcon />}
                          </Button>
                        </div>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  {failed && (
                    <p
                      role="alert"
                      className="-mt-1.5 flex items-center gap-1.5 text-[12.5px] text-destructive"
                    >
                      <CircleAlertIcon className="size-3.5" aria-hidden />
                      Check username and password.
                    </p>
                  )}
                  <Button
                    type="submit"
                    disabled={loginMutation.isPending}
                    className="mt-1 h-[46px] w-full rounded-[10px] text-[15px] font-semibold"
                  >
                    {loginMutation.isPending ? (
                      <>
                        <Spinner data-icon="inline-start" />
                        Signing in…
                      </>
                    ) : (
                      <>
                        Sign in
                        <ArrowRightIcon data-icon="inline-end" />
                      </>
                    )}
                  </Button>
                </form>
              </Form>
            </CollapsibleContent>
            {sso && !internalOn && trigger}
          </Collapsible>
        )}
      </div>
    </div>
  );
}
